using System.Threading.RateLimiting;
using Platform;
using Platform.Auth;

namespace Gateway;

public static class GatewayApp
{
    /// <param name="configure">Hook for tests to adjust services before the app is built.</param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults("gateway", connectionStringName: null);
        builder.AddServiceAuth();
        configure?.Invoke(builder);
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        var limits = builder.Configuration.GetSection("RateLimit");
        var window = TimeSpan.FromSeconds(limits.GetValue("WindowSeconds", 60));
        var authenticatedLimit = limits.GetValue("AuthenticatedPerWindow", 600);
        var anonymousLimit = limits.GetValue("AnonymousPerWindow", 60);

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Only successfully authenticated callers qualify for the larger bucket.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("health");

                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var authenticated = ctx.User.Identity?.IsAuthenticated == true;
                return RateLimitPartition.GetFixedWindowLimiter(
                    (authenticated ? "auth:" : "anon:") + ip,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authenticated ? authenticatedLimit : anonymousLimit,
                        Window = window,
                        QueueLimit = 0,
                    });
            });
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                await Results.Problem(statusCode: 429, title: "Too many requests").ExecuteAsync(context.HttpContext);
            };
        });

        var app = builder.Build();

        app.UseServiceDefaults();
        app.UseServiceAuth();
        app.UseRateLimiter();

        // Service-to-service endpoints are never reachable through the gateway, whatever the routes say.
        app.Use((ctx, next) =>
        {
            var segments = ctx.Request.Path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Contains("internal", StringComparer.OrdinalIgnoreCase)
                ? Results.Problem(statusCode: 404, title: "Not found").ExecuteAsync(ctx)
                : next();
        });

        // A request that carries a token must carry a valid one. Anonymous requests pass through;
        // the service decides (AllowAnonymous vs permission) and validates the token again.
        app.Use((ctx, next) =>
        {
            var hasToken = ctx.Request.Headers.Authorization.Count > 0;
            return hasToken && ctx.User.Identity?.IsAuthenticated != true
                ? Results.Problem(statusCode: 401, title: "Invalid or expired token").ExecuteAsync(ctx)
                : next();
        });

        app.MapReverseProxy();
        return app;
    }
}
