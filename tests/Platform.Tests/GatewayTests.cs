using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth;
using Shouldly;

namespace Platform.Tests;

/// <summary>Real gateway in front of a real downstream service (both on Kestrel, random ports).</summary>
public sealed class GatewayTests : IAsyncLifetime
{
    private const string Issuer = "http://localhost:8180/";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "gw-key" };
    private readonly RsaSecurityKey _otherKey = new(RSA.Create(2048)) { KeyId = "gw-key" };
    private WebApplication _downstream = null!;
    private WebApplication _gateway = null!;
    private HttpClient _client = null!;
    private int _downstreamHits;

    public async Task InitializeAsync()
    {
        _downstream = BuildDownstream();
        await _downstream.StartAsync();
        var downstreamUrl = AddressOf(_downstream);

        var args = new List<string>
        {
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", GatewayFolder(),
            "--RateLimit:AnonymousPerWindow=3",
            "--RateLimit:AuthenticatedPerWindow=50",
        };
        foreach (var cluster in new[] { "auth" })
            args.Add($"--ReverseProxy:Clusters:{cluster}:Destinations:d1:Address={downstreamUrl}");

        _gateway = GatewayApp.Build([.. args], b => UseTestKey(b.Services));
        await _gateway.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(AddressOf(_gateway)) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _gateway.DisposeAsync();
        await _downstream.DisposeAsync();
    }

    private static string GatewayFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RailwayLab.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Gateway");
    }

    private static string AddressOf(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    private void UseTestKey(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
            cfg.SigningKeys.Add(_key);
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            o.TokenValidationParameters.ValidIssuer = Issuer;
        });

    private WebApplication BuildDownstream()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
        builder.Configuration["Auth:RolePermissionsFile"] = PermissionSourceTests.MappingPath();
        builder.Configuration["Auth:Authority"] = Issuer;
        builder.Configuration["Auth:RequireHttpsMetadata"] = "false";
        builder.AddServiceAuth();
        UseTestKey(builder.Services);

        var app = builder.Build();
        app.Use((ctx, next) => { Interlocked.Increment(ref _downstreamHits); return next(); });
        app.UseWebSockets();
        app.UseServiceAuth();
        app.MapGet("/api/auth/public", () => "stations").AllowAnonymous();
        app.MapGet("/api/auth/holds", (System.Security.Claims.ClaimsPrincipal u) => u.GetUserId().ToString())
            .RequirePermission(Permissions.SeatsHold);
        app.MapGet("/api/auth/users", () => "users").RequirePermission(Permissions.AuthManage);
        app.MapGet("/api/auth/internal/secret", () => "leak").AllowAnonymous();
        app.MapGet("/internal/auth/role-permissions", () => "leak").AllowAnonymous();
        app.Map("/api/auth/echo", async ctx =>
        {
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[256];
            var r = await socket.ReceiveAsync(buffer, CancellationToken.None);
            await socket.SendAsync(buffer.AsMemory(0, r.Count), WebSocketMessageType.Text, true, CancellationToken.None);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        });
        return app;
    }

    private string Token(string[]? roles = null, SecurityKey? signWith = null, string? sub = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "railway-api",
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signWith ?? _key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub ?? Guid.NewGuid().ToString(),
                ["role"] = roles ?? ["passenger"],
            },
        });
    }

    private Task<HttpResponseMessage> Get(string path, string? token = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(req);
    }

    [Fact]
    public async Task Anonymous_on_a_public_route_is_200()
        => (await Get("/api/auth/public")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Anonymous_on_a_protected_route_is_401_from_the_service()
        => (await Get("/api/auth/holds")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Valid_token_without_the_permission_is_403()
        => (await Get("/api/auth/users", Token())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Valid_token_with_the_permission_reaches_the_service_with_the_user_id()
    {
        var sub = Guid.NewGuid().ToString();
        var res = await Get("/api/auth/holds", Token(sub: sub));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ShouldBe(sub);
    }

    [Fact]
    public async Task Invalid_token_is_rejected_at_the_gateway_without_reaching_the_service()
    {
        var before = _downstreamHits;
        var res = await Get("/api/auth/public", Token(signWith: _otherKey));
        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _downstreamHits.ShouldBe(before);
    }

    [Theory]
    [InlineData("/internal/auth/role-permissions")]
    [InlineData("/api/auth/internal/secret")]
    [InlineData("/api/auth/INTERNAL/secret")]
    public async Task Internal_paths_are_never_routed(string path)
    {
        var before = _downstreamHits;
        (await Get(path, Token(roles: ["admin"]))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _downstreamHits.ShouldBe(before);
    }

    [Fact]
    public async Task Unrouted_paths_are_404()
        => (await Get("/api/unknown/x", Token())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    public async Task Anonymous_traffic_is_limited_more_strictly_than_authenticated()
    {
        for (var i = 0; i < 3; i++)
            (await Get("/api/auth/public")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var limited = await Get("/api/auth/public");
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").ShouldBeTrue();
        (await limited.Content.ReadAsStringAsync()).ShouldContain("Too many requests");

        var token = Token();
        for (var i = 0; i < 10; i++)
            (await Get("/api/auth/holds", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_tokens_do_not_receive_the_authenticated_rate_limit()
    {
        for (var i = 0; i < 3; i++)
        {
            (await Get("/api/auth/public", "invalid-token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await Get("/api/auth/public", "invalid-token")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        _downstreamHits.ShouldBe(0);

        (await Get("/api/auth/public", Token())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_is_not_rate_limited()
    {
        for (var i = 0; i < 10; i++)
            (await Get("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebSockets_pass_through_the_gateway()
    {
        using var ws = new ClientWebSocket();
        var uri = new UriBuilder(_client.BaseAddress!) { Scheme = "ws", Path = "/api/auth/echo" }.Uri;
        await ws.ConnectAsync(uri, CancellationToken.None);
        await ws.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[64];
        var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
        Encoding.UTF8.GetString(buffer, 0, r.Count).ShouldBe("ping");
    }
}
