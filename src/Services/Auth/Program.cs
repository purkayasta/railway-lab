using AuthService.Data;
using AuthService.Oidc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Platform;
using Platform.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("auth");
builder.AddPermissionAuthorization();

var config = builder.Configuration.GetSection("Auth");
var issuer = config["Issuer"] ?? throw new InvalidOperationException("Auth:Issuer is required (the public base URL of this service).");
var requireHttps = config.GetValue("RequireHttpsMetadata", true);
var keys = SigningKeys.LoadOrCreate(config["KeyDirectory"]);
var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(config["WebOrigin"] ?? "http://localhost:4200")
    .AllowAnyHeader()
    .WithMethods("GET", "POST")));

builder.Services.AddDbContext<AuthDbContext>(o =>
{
    o.UseSqlServer(
        connectionString ?? throw new InvalidOperationException("ConnectionStrings:Default is required."),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema));
    o.UseOpenIddict();
});

builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        // Brute-force protection: 5 failures lock the account for 15 minutes.
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        // Same claim names as the tokens we issue.
        o.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.PreferredUsername;
        o.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
        o.ClaimsIdentity.RoleClaimType = OpenIddictConstants.Claims.Role;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// The Identity cookie is the single sign-on session at the Auth service. Checking the security stamp on every
// request makes "sign out everywhere" take effect immediately in every browser.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.Cookie.Name = "railway.sso";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax; // the redirect back from the SPA is a top-level navigation
    o.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
});

// API calls to this service carry a bearer access token; protocol pages use the cookie explicitly.
builder.Services.AddAuthentication(o =>
{
    // Identity sets its own authenticate/challenge defaults, so override each explicitly.
    o.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    o.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    o.DefaultForbidScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
    .AddServer(o =>
    {
        o.SetIssuer(new Uri(issuer));
        o.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetUserInfoEndpointUris("connect/userinfo")
            .SetEndSessionEndpointUris("connect/logout")
            .SetRevocationEndpointUris("connect/revocation");

        o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowClientCredentialsFlow();
        o.Configure(c => c.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain)); // S256 only
        o.RegisterScopes(OpenIddictConstants.Scopes.Email, OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Roles, OpenIddictConstants.Scopes.OfflineAccess);

        o.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(1))
            .SetAccessTokenLifetime(TimeSpan.FromMinutes(5))
            .SetIdentityTokenLifetime(TimeSpan.FromMinutes(5))
            .SetRefreshTokenLifetime(TimeSpan.FromDays(14))
            .SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(config.GetValue("RefreshTokenReuseLeewaySeconds", 10)));

        // Access tokens are signed JWTs that other services validate with the public key from /.well-known/jwks.
        // Refresh tokens and codes stay encrypted: only this service reads them.
        o.DisableAccessTokenEncryption();
        o.AddSigningKey(keys.Signing).AddEncryptionKey(keys.Encryption);

        var aspNet = o.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();
        if (!requireHttps) aspNet.DisableTransportSecurityRequirement();
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();
        o.UseAspNetCore();
        // Our own API rejects a revoked access token immediately; other services rely on the 5 minute lifetime.
        o.EnableTokenEntryValidation();
        o.EnableAuthorizationEntryValidation();
    });

builder.Services.AddRazorPages().AddApplicationPart(typeof(Program).Assembly);
builder.Services.AddHostedService<AuthInitializer>();

var app = builder.Build();
app.UseServiceDefaults();
app.Use(SecurityHeaders);
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapConnectEndpoints();
app.MapSessionEndpoints();
app.MapRazorPages().AllowAnonymous();

await app.RunAsync();

Task SecurityHeaders(HttpContext ctx, RequestDelegate next)
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    // No form-action here: browsers apply it to the redirect chain after the login POST, which ends at the SPA.
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
    return next(ctx);
}

/// <summary>Entry point type, exposed so tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
