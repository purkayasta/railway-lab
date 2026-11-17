using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth;
using Shouldly;

namespace Platform.Tests;

/// <summary>Validates JWTs the way tokens from the Auth service are validated, using a locally generated signing key.</summary>
public sealed class JwtValidationTests : IAsyncLifetime
{
    private const string Issuer = "http://localhost:8180/";
    private const string Audience = "railway-api";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly RsaSecurityKey _otherKey = new(RSA.Create(2048)) { KeyId = "test-key" }; // same kid, different key
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["Auth:RolePermissionsFile"] = PermissionSourceTests.MappingPath();
        builder.Configuration["Auth:Authority"] = Issuer;
        builder.Configuration["Auth:RequireHttpsMetadata"] = "false";
        builder.AddServiceAuth();
        // Skip OIDC discovery: hand the validator the issuer and the public key directly.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
            cfg.SigningKeys.Add(_key);
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            o.TokenValidationParameters.ValidIssuer = Issuer;
        });

        _app = builder.Build();
        _app.UseServiceAuth();
        _app.MapGet("/public", () => "ok").AllowAnonymous();
        _app.MapGet("/hold", () => "held").RequirePermission(Permissions.SeatsHold);
        _app.MapGet("/admin", () => "admin").RequirePermission(Permissions.AuthManage);
        _app.MapGet("/me", (System.Security.Claims.ClaimsPrincipal u) => u.GetUserId().ToString()).RequirePermission(Permissions.SeatsHold);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private string Token(
        string[]? roles = null, string? issuer = null, string? audience = null, SecurityKey? signWith = null,
        DateTime? expires = null, string? sub = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            NotBefore = now.AddMinutes(-10),
            IssuedAt = now.AddMinutes(-10),
            Expires = expires ?? now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signWith ?? _key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub ?? Guid.NewGuid().ToString(),
                ["preferred_username"] = "alice",
                ["role"] = roles ?? ["passenger"],
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpStatusCode> Get(string path, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        return res.StatusCode;
    }

    [Fact]
    public async Task Anonymous_can_reach_an_allowlisted_public_endpoint()
        => (await Get("/public")).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Missing_token_on_a_protected_endpoint_is_401()
        => (await Get("/hold")).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Valid_passenger_token_reaches_a_permitted_endpoint()
        => (await Get("/hold", Token())).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Passenger_on_an_admin_endpoint_is_403()
        => (await Get("/admin", Token())).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Admin_token_reaches_the_admin_endpoint()
        => (await Get("/admin", Token(["admin"]))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Role_names_alone_do_not_authorize_unknown_roles_get_403()
        => (await Get("/hold", Token(["service"]))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Token_without_roles_is_403()
        => (await Get("/hold", Token([]))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Wrong_issuer_is_rejected()
        => (await Get("/hold", Token(issuer: "http://evil.test/realms/railway"))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Wrong_audience_is_rejected()
        => (await Get("/hold", Token(audience: "some-other-api"))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
        => (await Get("/hold", Token(signWith: _otherKey))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Expired_token_is_rejected()
        => (await Get("/hold", Token(expires: DateTime.UtcNow.AddMinutes(-5)))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Unsigned_token_is_rejected()
    {
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            iss = Issuer,
            aud = Audience,
            sub = Guid.NewGuid(),
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            role = new[] { "admin" },
        }));
        (await Get("/admin", $"{header}.{payload}.")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_expired_within_clock_skew_is_still_accepted()
        => (await Get("/hold", Token(expires: DateTime.UtcNow.AddSeconds(-10)))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Sub_claim_is_the_user_id()
    {
        var sub = Guid.NewGuid();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(sub: sub.ToString()));
        var res = await _client.SendAsync(req);
        (await res.Content.ReadAsStringAsync()).ShouldBe(sub.ToString());
    }

    private static string Base64Url(string s) => Base64UrlEncoder.Encode(s);
}
