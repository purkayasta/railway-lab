using System.Net;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Auth.Tests;

/// <summary>OpenID Connect discovery and the Authorization Code + PKCE flow, step by step.</summary>
public sealed class AuthorizationCodeFlowTests : IAsyncLifetime
{
    private AuthServer _server = null!;

    public async Task InitializeAsync() => _server = await AuthServer.StartAsync();
    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Discovery_document_describes_the_server()
    {
        var doc = JsonDocument.Parse(await _server.CreateClient().GetStringAsync("/.well-known/openid-configuration")).RootElement;

        doc.GetProperty("issuer").GetString().ShouldBe(AuthServer.Issuer);
        doc.GetProperty("authorization_endpoint").GetString().ShouldEndWith("/connect/authorize");
        doc.GetProperty("token_endpoint").GetString().ShouldEndWith("/connect/token");
        doc.GetProperty("jwks_uri").GetString().ShouldEndWith("/.well-known/jwks");
        doc.GetProperty("end_session_endpoint").GetString().ShouldEndWith("/connect/logout");
        doc.GetProperty("revocation_endpoint").GetString().ShouldEndWith("/connect/revocation");
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).ShouldBe(["S256"]);
        doc.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["authorization_code", "client_credentials", "refresh_token"], ignoreOrder: true);
    }

    [Fact]
    public async Task Jwks_publishes_only_public_key_material()
    {
        var jwks = await _server.CreateClient().GetStringAsync("/.well-known/jwks");
        var key = JsonDocument.Parse(jwks).RootElement.GetProperty("keys")[0];

        key.GetProperty("kty").GetString().ShouldBe("RSA");
        key.TryGetProperty("d", out _).ShouldBeFalse("private exponent must never be published");
    }

    [Fact]
    public async Task Anonymous_authorize_request_is_sent_to_the_login_page_and_back()
    {
        var browser = _server.NewBrowser();
        var pkce = Pkce.Create();
        var authorizeUrl = OidcFlow.AuthorizeUrl(pkce);

        var response = await browser.Get(authorizeUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = Browser.LocationOf(response);
        login.AbsolutePath.ShouldBe("/account/login");
        OidcFlow.Param(login, "ReturnUrl").ShouldStartWith("/connect/authorize?");
    }

    [Fact]
    public async Task Full_flow_returns_tokens_with_the_expected_claims()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");

        tokens.GetProperty("token_type").GetString().ShouldBe("Bearer");
        tokens.GetProperty("expires_in").GetInt32().ShouldBeInRange(295, 300);
        tokens.TryGetProperty("refresh_token", out var refresh).ShouldBeTrue();
        refresh.GetString().ShouldNotBeNullOrEmpty();

        var access = new JsonWebToken(tokens.GetProperty("access_token").GetString());
        access.Issuer.ShouldBe(AuthServer.Issuer);
        access.Audiences.ShouldBe(["railway-api"]);
        access.GetClaim("preferred_username").Value.ShouldBe("alice");
        access.Claims.Where(c => c.Type == "role").Select(c => c.Value).ShouldBe(["passenger"]);
        Guid.TryParse(access.GetClaim("sub").Value, out _).ShouldBeTrue();

        var id = new JsonWebToken(tokens.GetProperty("id_token").GetString());
        id.Audiences.ShouldBe(["web"]);
        id.GetClaim("nonce").Value.ShouldBe("nonce-123");
        id.Claims.Where(c => c.Type == "role").Select(c => c.Value).ShouldBe(["passenger"]);
    }

    [Fact]
    public async Task Access_token_verifies_against_the_published_jwks()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "sam");
        var jwks = new JsonWebKeySet(await _server.CreateClient().GetStringAsync("/.well-known/jwks"));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.GetProperty("access_token").GetString(),
            new TokenValidationParameters
            {
                ValidIssuer = AuthServer.Issuer,
                ValidAudience = "railway-api",
                IssuerSigningKeys = jwks.GetSigningKeys(),
            });

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        result.Claims["role"].ShouldBe("station-agent");
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_a_generic_message_and_no_session()
    {
        var browser = _server.NewBrowser();
        var login = Browser.LocationOf(await browser.Get(OidcFlow.AuthorizeUrl(Pkce.Create())));

        var response = await OidcFlow.SubmitLogin(browser, login, "alice", "not-the-password");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Invalid username or password.");
        browser.Cookie("railway.sso").ShouldBeNull();

        // An unknown user gets exactly the same answer: no account enumeration.
        var unknown = await (await OidcFlow.SubmitLogin(browser, login, "nobody", "whatever-pass")).Content.ReadAsStringAsync();
        unknown.ShouldContain("Invalid username or password.");
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_even_for_the_right_password()
    {
        var browser = _server.NewBrowser();
        var login = Browser.LocationOf(await browser.Get(OidcFlow.AuthorizeUrl(Pkce.Create())));
        for (var i = 0; i < 5; i++)
            await OidcFlow.SubmitLogin(browser, login, "bob", "wrong-password-" + i);

        var response = await OidcFlow.SubmitLogin(browser, login, "bob", AuthServer.Password);

        (await response.Content.ReadAsStringAsync()).ShouldContain("Too many failed attempts");
        browser.Cookie("railway.sso").ShouldBeNull();
    }

    [Fact]
    public async Task Authorization_code_can_be_used_only_once()
    {
        var browser = _server.NewBrowser();
        var pkce = Pkce.Create();
        var code = OidcFlow.Param(await OidcFlow.Authorize(browser, OidcFlow.AuthorizeUrl(pkce), "alice"), "code");

        (await OidcFlow.RedeemCode(_server.CreateClient(), code, pkce)).Status.ShouldBe(200);
        var second = await OidcFlow.RedeemCode(_server.CreateClient(), code, pkce);

        second.Status.ShouldBe(400);
        second.Body.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task Wrong_pkce_verifier_is_rejected()
    {
        var browser = _server.NewBrowser();
        var pkce = Pkce.Create();
        var code = OidcFlow.Param(await OidcFlow.Authorize(browser, OidcFlow.AuthorizeUrl(pkce), "alice"), "code");

        var attack = await OidcFlow.RedeemCode(_server.CreateClient(), code, Pkce.Create()); // a thief without the verifier

        attack.Status.ShouldBe(400);
        attack.Body.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task Authorize_request_without_pkce_is_rejected()
    {
        var url = System.Text.RegularExpressions.Regex.Replace(
            OidcFlow.AuthorizeUrl(Pkce.Create()), "&code_challenge(_method)?=[^&]+", "");
        var response = await _server.NewBrowser().Get(url);
        // The request is refused outright: no login page, no code, nothing issued.
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_request");
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Unregistered_redirect_uri_is_never_redirected_to()
    {
        var response = await _server.NewBrowser().Get(
            OidcFlow.AuthorizeUrl(Pkce.Create(), redirectUri: "http://evil.test/callback"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_client_is_rejected()
    {
        var response = await _server.NewBrowser().Get(OidcFlow.AuthorizeUrl(Pkce.Create(), clientId: "mallory"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Prompt_none_without_a_session_returns_login_required()
    {
        var response = await _server.NewBrowser().Get(OidcFlow.AuthorizeUrl(Pkce.Create(), extra: "prompt=none"));

        OidcFlow.Param(Browser.LocationOf(response), "error").ShouldBe("login_required");
    }

    [Fact]
    public async Task Existing_session_gets_a_code_without_asking_for_credentials_again()
    {
        var (browser, _, _) = await OidcFlow.SignIn(_server, "alice");

        var again = await browser.Get(OidcFlow.AuthorizeUrl(Pkce.Create()));

        OidcFlow.Param(Browser.LocationOf(again), "code").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Userinfo_returns_claims_allowed_by_the_granted_scopes()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "olivia");
        var http = _server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());

        var info = JsonDocument.Parse(await http.GetStringAsync("/connect/userinfo")).RootElement;

        info.GetProperty("preferred_username").GetString().ShouldBe("olivia");
        info.GetProperty("email").GetString().ShouldBe("olivia@railway.test");
        info.GetProperty("role").EnumerateArray().Select(e => e.GetString()).ShouldBe(["operations"]);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_only_redirects_to_registered_uris()
    {
        var (browser, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        var idToken = tokens.GetProperty("id_token").GetString();

        var evil = await browser.Get($"/connect/logout?id_token_hint={idToken}&post_logout_redirect_uri=http://evil.test/");
        evil.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await browser.Get($"/connect/logout?id_token_hint={idToken}&post_logout_redirect_uri={Uri.EscapeDataString(AuthServer.PostLogoutUri)}");
        Browser.LocationOf(ok).GetLeftPart(UriPartial.Path).ShouldBe(AuthServer.PostLogoutUri);

        // The single sign-on cookie is gone: the next authorize request needs credentials again.
        var next = await browser.Get(OidcFlow.AuthorizeUrl(Pkce.Create()));
        Browser.LocationOf(next).AbsolutePath.ShouldBe("/account/login");
    }
}
