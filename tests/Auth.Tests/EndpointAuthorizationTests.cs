using AuthService;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth;
using Shouldly;

namespace Auth.Tests;

public sealed class EndpointAuthorizationTests
{
    // OIDC protocol endpoints and the login pages authenticate in their own way (cookie, PKCE, client secret, token
    // introspection by OpenIddict), so they are not permission-protected. Everything under /api must be.
    [Fact]
    public async Task Every_api_endpoint_declares_a_permission_from_the_catalog()
    {
        await using var server = await AuthServer.StartAsync();
        var endpoints = server.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

        var violations = EndpointAuthorizationAudit.Audit(
            endpoints, new HashSet<string>(), StaticPermissionSource.FromFile(AuthServer.MappingPath()));

        endpoints.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }
}
