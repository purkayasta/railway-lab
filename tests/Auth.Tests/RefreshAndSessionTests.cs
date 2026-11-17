using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using AuthService.Data;
using Shouldly;

namespace Auth.Tests;

/// <summary>Refresh token rotation, reuse detection, revocation and "sign out of all devices".</summary>
public sealed class RefreshAndSessionTests : IAsyncLifetime
{
    private AuthServer _server = null!;

    public async Task InitializeAsync() => _server = await AuthServer.StartAsync();
    public async Task DisposeAsync() => await _server.DisposeAsync();

    private static string Str(JsonElement tokens, string name) => tokens.GetProperty(name).GetString()!;

    private HttpClient Bearer(string accessToken)
    {
        var http = _server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    [Fact]
    public async Task Refresh_returns_a_new_pair_and_the_old_refresh_token_stops_working()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        var first = Str(tokens, "refresh_token");

        var (status, renewed) = await OidcFlow.Refresh(_server.CreateClient(), first);

        status.ShouldBe(200);
        Str(renewed, "refresh_token").ShouldNotBe(first);
        Str(renewed, "access_token").ShouldNotBe(Str(tokens, "access_token"));
        (await OidcFlow.Refresh(_server.CreateClient(), first)).Status.ShouldBe(400);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_chain()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        var stolen = Str(tokens, "refresh_token");
        var (_, renewed) = await OidcFlow.Refresh(_server.CreateClient(), stolen);
        var legitimate = Str(renewed, "refresh_token");

        // Someone replays the old token: refused, and the family is revoked as a precaution.
        var replay = await OidcFlow.Refresh(_server.CreateClient(), stolen);
        replay.Status.ShouldBe(400);
        replay.Body.GetProperty("error").GetString().ShouldBe("invalid_grant");

        (await OidcFlow.Refresh(_server.CreateClient(), legitimate)).Status.ShouldBe(400);
    }

    [Fact]
    public async Task Role_changes_reach_the_user_with_the_next_refresh()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        await using (var scope = _server.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.AddToRoleAsync((await users.FindByNameAsync("alice"))!, "operations");
        }

        var (_, renewed) = await OidcFlow.Refresh(_server.CreateClient(), Str(tokens, "refresh_token"));

        new JsonWebToken(Str(renewed, "access_token")).Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .ShouldBe(["passenger", "operations"], ignoreOrder: true);
    }

    [Fact]
    public async Task Refresh_stops_for_a_user_who_is_locked_out_afterwards()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        await using (var scope = _server.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByNameAsync("alice"))!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var (status, body) = await OidcFlow.Refresh(_server.CreateClient(), Str(tokens, "refresh_token"));

        status.ShouldBe(400);
        body.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task Revocation_endpoint_invalidates_a_single_refresh_token()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        var refresh = Str(tokens, "refresh_token");

        var revoke = await _server.CreateClient().PostAsync("/connect/revocation", new FormUrlEncodedContent(
        [
            new("client_id", "web"), new("token", refresh), new("token_type_hint", "refresh_token"),
        ]));

        revoke.IsSuccessStatusCode.ShouldBeTrue();
        (await OidcFlow.Refresh(_server.CreateClient(), refresh)).Status.ShouldBe(400);
    }

    [Fact]
    public async Task Sign_out_everywhere_revokes_every_device_and_browser_session()
    {
        // Two devices: separate browsers, separate refresh tokens.
        var (phoneBrowser, phone, _) = await OidcFlow.SignIn(_server, "alice");
        var (laptopBrowser, laptop, _) = await OidcFlow.SignIn(_server, "alice");
        var (_, other, _) = await OidcFlow.SignIn(_server, "bob");

        var response = await Bearer(Str(laptop, "access_token")).PostAsync("/api/auth/sessions/revoke-all", null);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        // Refresh tokens of both devices are dead.
        (await OidcFlow.Refresh(_server.CreateClient(), Str(phone, "refresh_token"))).Status.ShouldBe(400);
        (await OidcFlow.Refresh(_server.CreateClient(), Str(laptop, "refresh_token"))).Status.ShouldBe(400);

        // Both browsers lose their single sign-on session: no silent code, back to the login page.
        foreach (var browser in new[] { phoneBrowser, laptopBrowser })
        {
            var again = await browser.Get(OidcFlow.AuthorizeUrl(Pkce.Create()));
            Browser.LocationOf(again).AbsolutePath.ShouldBe("/account/login");
        }

        // The Auth API itself rejects the revoked access token immediately.
        (await Bearer(Str(laptop, "access_token")).PostAsync("/api/auth/sessions/revoke-all", null))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);

        // Other users are untouched.
        (await OidcFlow.Refresh(_server.CreateClient(), Str(other, "refresh_token"))).Status.ShouldBe(200);
    }

    [Fact]
    public async Task Signing_in_again_after_sign_out_everywhere_works()
    {
        var (_, tokens, _) = await OidcFlow.SignIn(_server, "alice");
        await Bearer(Str(tokens, "access_token")).PostAsync("/api/auth/sessions/revoke-all", null);

        var (_, fresh, _) = await OidcFlow.SignIn(_server, "alice");

        (await OidcFlow.Refresh(_server.CreateClient(), Str(fresh, "refresh_token"))).Status.ShouldBe(200);
    }

    [Fact]
    public async Task Revoke_all_requires_a_valid_token_with_the_permission()
    {
        (await _server.CreateClient().PostAsync("/api/auth/sessions/revoke-all", null))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);

        // A machine token carries the "service" role, which is not mapped to sessions:revoke-own.
        var (_, service) = await OidcFlow.Token(_server.CreateClient(),
            ("grant_type", "client_credentials"), ("client_id", "internal-services"), ("client_secret", AuthServer.InternalSecret));
        (await Bearer(Str(service, "access_token")).PostAsync("/api/auth/sessions/revoke-all", null))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_credentials_issue_a_service_token_and_reject_a_wrong_secret()
    {
        var (status, token) = await OidcFlow.Token(_server.CreateClient(),
            ("grant_type", "client_credentials"), ("client_id", "internal-services"), ("client_secret", AuthServer.InternalSecret));

        status.ShouldBe(200);
        var jwt = new JsonWebToken(Str(token, "access_token"));
        jwt.Audiences.ShouldBe(["railway-api"]);
        jwt.GetClaim("role").Value.ShouldBe("service");
        token.TryGetProperty("refresh_token", out _).ShouldBeFalse();

        var bad = await OidcFlow.Token(_server.CreateClient(),
            ("grant_type", "client_credentials"), ("client_id", "internal-services"), ("client_secret", "wrong"));
        bad.Status.ShouldBe(401); // invalid_client is answered with 401 (RFC 6749 section 5.2)
        bad.Body.GetProperty("error").GetString().ShouldBe("invalid_client");
    }

    [Fact]
    public async Task The_public_web_client_cannot_use_client_credentials()
    {
        var (status, body) = await OidcFlow.Token(_server.CreateClient(), ("grant_type", "client_credentials"), ("client_id", "web"));

        status.ShouldBe(400);
        body.GetProperty("error").GetString().ShouldBeOneOf("unauthorized_client", "invalid_client", "invalid_request");
    }
}
