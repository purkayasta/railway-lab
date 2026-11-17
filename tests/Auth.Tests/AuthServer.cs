using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuthService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Auth.Tests;

/// <summary>The real Auth service running in-process on an in-memory SQLite database.</summary>
public sealed class AuthServer : IAsyncDisposable
{
    public const string Issuer = "http://localhost/";
    public const string Password = "Test-Passw0rd!x";
    public const string InternalSecret = "internal-secret-for-tests";
    public const string RedirectUri = "http://localhost:4200/callback";
    public const string PostLogoutUri = "http://localhost:4200/";
    public const string AllScopes = "openid profile email roles offline_access";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection;

    private AuthServer(WebApplicationFactory<Program> factory, SqliteConnection connection) =>
        (_factory, _connection) = (factory, connection);

    public IServiceProvider Services => _factory.Services;
    public HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    public Browser NewBrowser() => new(CreateClient());

    public static async Task<AuthServer> StartAsync(int refreshReuseLeewaySeconds = 0)
    {
        // SQLite keeps the real relational behaviour (bulk updates) that OpenIddict relies on for revocation.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("Auth:Issuer", Issuer);
            host.UseSetting("Auth:RequireHttpsMetadata", "false");
            host.UseSetting("Auth:SeedUserPassword", Password);
            host.UseSetting("Auth:InternalServicesClientSecret", InternalSecret);
            host.UseSetting("Auth:RefreshTokenReuseLeewaySeconds", refreshReuseLeewaySeconds.ToString());
            host.UseSetting("Auth:RolePermissionsFile", MappingPath());
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                services.AddDbContext<AuthDbContext>(o =>
                {
                    o.UseSqlite(connection);
                    o.UseOpenIddict();
                });
            });
        });
        _ = factory.Server; // starts the host, which runs the initializer
        return new AuthServer(factory, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static string MappingPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RailwayLab.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "deploy", "auth", "default-role-permissions.json");
    }
}

/// <summary>Minimal browser: keeps cookies, never follows redirects on its own, so tests can look at every hop.</summary>
public sealed class Browser(HttpClient http)
{
    private readonly Dictionary<string, string> _cookies = [];

    public string? Cookie(string name) => _cookies.GetValueOrDefault(name);

    public async Task<HttpResponseMessage> Send(HttpRequestMessage request)
    {
        if (_cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await http.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var header in setCookies)
            {
                var pair = header.Split(';', 2)[0];
                var eq = pair.IndexOf('=');
                var (name, value) = (pair[..eq], pair[(eq + 1)..]);
                if (value.Length == 0) _cookies.Remove(name); else _cookies[name] = value;
            }
        return response;
    }

    public Task<HttpResponseMessage> Get(string url) => Send(new HttpRequestMessage(HttpMethod.Get, url));

    public Task<HttpResponseMessage> Post(string url, IEnumerable<KeyValuePair<string, string>> form) =>
        Send(new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) });

    public static Uri LocationOf(HttpResponseMessage response) =>
        new(new Uri("http://localhost/"), response.Headers.Location!.ToString());
}

/// <summary>PKCE pair (RFC 7636): the client keeps the verifier secret and sends only its SHA-256 hash first.</summary>
public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        return new(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class OidcFlow
{
    public static string AuthorizeUrl(Pkce pkce, string scope = AuthServer.AllScopes, string clientId = "web",
        string redirectUri = AuthServer.RedirectUri, string state = "state-123", string? extra = null) =>
        "/connect/authorize?" + string.Join('&',
            $"client_id={clientId}", "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}", $"scope={Uri.EscapeDataString(scope)}",
            $"code_challenge={pkce.Challenge}", "code_challenge_method=S256",
            $"state={state}", "nonce=nonce-123") + (extra is null ? "" : "&" + extra);

    /// <summary>Authorize request, then (if needed) the login form. Returns the final redirect back to the client.</summary>
    public static async Task<Uri> Authorize(Browser browser, string authorizeUrl, string? username = null, string password = AuthServer.Password)
    {
        var response = await browser.Get(authorizeUrl);
        var location = Browser.LocationOf(response);

        if (location.AbsolutePath == "/account/login" && username is not null)
        {
            var login = await SubmitLogin(browser, location, username, password);
            if (login.StatusCode != System.Net.HttpStatusCode.Redirect)
                throw new InvalidOperationException($"Login did not redirect: {(int)login.StatusCode} {(await login.Content.ReadAsStringAsync())[..Math.Min(600, (await login.Content.ReadAsStringAsync()).Length)]}");
            response = await browser.Get(Browser.LocationOf(login).PathAndQuery);
            location = Browser.LocationOf(response);
        }
        return location;
    }

    public static async Task<HttpResponseMessage> SubmitLogin(Browser browser, Uri loginUrl, string username, string password)
    {
        var page = await (await browser.Get(loginUrl.PathAndQuery)).Content.ReadAsStringAsync();
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var returnUrl = System.Web.HttpUtility.ParseQueryString(loginUrl.Query)["ReturnUrl"] ?? "";
        return await browser.Post(loginUrl.PathAndQuery,
        [
            new("Username", username), new("Password", password), new("ReturnUrl", returnUrl),
            new("__RequestVerificationToken", token),
        ]);
    }

    public static string Param(Uri uri, string name) => System.Web.HttpUtility.ParseQueryString(uri.Query)[name] ?? "";

    public static async Task<(int Status, JsonElement Body)> Token(HttpClient http, params (string Key, string Value)[] form)
    {
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ((int)response.StatusCode, doc.RootElement.Clone());
    }

    public static Task<(int Status, JsonElement Body)> RedeemCode(HttpClient http, string code, Pkce pkce) =>
        Token(http, ("grant_type", "authorization_code"), ("client_id", "web"), ("code", code),
            ("redirect_uri", AuthServer.RedirectUri), ("code_verifier", pkce.Verifier));

    public static Task<(int Status, JsonElement Body)> Refresh(HttpClient http, string refreshToken) =>
        Token(http, ("grant_type", "refresh_token"), ("client_id", "web"), ("refresh_token", refreshToken));

    /// <summary>The whole happy path: login as <paramref name="username"/> and exchange the code for tokens.</summary>
    public static async Task<(Browser Browser, JsonElement Tokens, Pkce Pkce)> SignIn(AuthServer server, string username)
    {
        var browser = server.NewBrowser();
        var pkce = Pkce.Create();
        var redirect = await Authorize(browser, AuthorizeUrl(pkce), username);
        var (status, tokens) = await RedeemCode(server.CreateClient(), Param(redirect, "code"), pkce);
        if (status != 200) throw new InvalidOperationException($"Token exchange failed: {tokens}");
        return (browser, tokens, pkce);
    }
}
