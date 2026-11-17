using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Auth;

public static class AuthDefaults
{
    /// <summary>
    /// JWT bearer validation against the Auth service (discovery and JWKS from <c>Auth:Authority</c>) and
    /// permission-based authorization. Configuration: Auth:Authority, Auth:Issuer (expected <c>iss</c>, defaults to the
    /// authority; set it when services reach the Auth service by a different address than the browser does),
    /// Auth:Audience (default railway-api), Auth:RequireHttpsMetadata (default true), Auth:RolePermissionsFile.
    /// </summary>
    public static WebApplicationBuilder AddServiceAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Auth");
        var audience = config["Audience"] ?? "railway-api";

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = config["Authority"];
                o.RequireHttpsMetadata = config.GetValue("RequireHttpsMetadata", true);
                if (RewriteToAuthority.TryCreate(config["Authority"], config["Issuer"]) is { } rewrite)
                    o.BackchannelHttpHandler = rewrite;
                o.MapInboundClaims = false; // keep "sub" and "role" as issued
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = config["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = RoleClaim,
                };
            });

        return builder.AddPermissionAuthorization();
    }

    /// <summary>Permission policies and the role-to-permission source, without choosing how callers authenticate.</summary>
    public static WebApplicationBuilder AddPermissionAuthorization(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Auth");
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IPermissionSource>(_ =>
        {
            var path = config["RolePermissionsFile"] ?? Path.Combine(AppContext.BaseDirectory, "default-role-permissions.json");
            return StaticPermissionSource.FromFile(path);
        });
        return builder;
    }

    public static WebApplication UseServiceAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Roles arrive as <c>role</c> claims in access tokens issued by the Auth service.</summary>
    public const string RoleClaim = "role";
}

/// <summary>
/// The discovery document advertises URLs under the public issuer (for example http://localhost:8180), which a
/// container cannot reach. Requests for the issuer's origin are sent to the internal authority address instead.
/// </summary>
internal sealed class RewriteToAuthority(Uri issuer, Uri authority) : DelegatingHandler(new HttpClientHandler())
{
    public static RewriteToAuthority? TryCreate(string? authority, string? issuer)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var a) || !Uri.TryCreate(issuer, UriKind.Absolute, out var i))
            return null;
        return a.GetLeftPart(UriPartial.Authority) == i.GetLeftPart(UriPartial.Authority) ? null : new RewriteToAuthority(i, a);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && uri.GetLeftPart(UriPartial.Authority) == issuer.GetLeftPart(UriPartial.Authority))
            request.RequestUri = new UriBuilder(uri) { Scheme = authority.Scheme, Host = authority.Host, Port = authority.Port }.Uri;
        return base.SendAsync(request, cancellationToken);
    }
}
