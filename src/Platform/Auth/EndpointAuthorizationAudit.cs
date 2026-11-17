using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Platform.Auth;

/// <summary>
/// Default-deny guard. Services call this from a test with their mapped endpoints; any violation fails the test.
/// Anonymous endpoints must be GET/HEAD/OPTIONS only and listed in a reviewed allowlist ("GET /route/pattern").
/// </summary>
public static class EndpointAuthorizationAudit
{
    private static readonly HashSet<string> ReadOnlyMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public static IReadOnlyList<string> Audit(
        IEnumerable<Endpoint> endpoints,
        IReadOnlySet<string> anonymousAllowlist,
        StaticPermissionSource source)
    {
        var violations = new List<string>();
        var adminPermissions = source.GetPermissions(["admin"]);

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            var route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "?";
            var name = $"{(methods.Count == 0 ? "ANY" : string.Join(",", methods))} {route}";

            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var permissions = endpoint.Metadata.GetOrderedMetadata<RequiredPermissionMetadata>().Select(m => m.Permission).ToList();

            if (anonymous && permissions.Count > 0)
                violations.Add($"{name}: declares both AllowAnonymous and a permission.");

            if (anonymous)
            {
                if (methods.Count == 0 || methods.Any(m => !ReadOnlyMethods.Contains(m)))
                    violations.Add($"{name}: AllowAnonymous endpoints must be GET/HEAD/OPTIONS only.");
                foreach (var m in methods)
                    if (!anonymousAllowlist.Contains($"{m.ToUpperInvariant()} {route}"))
                        violations.Add($"{name}: AllowAnonymous but not on the reviewed allowlist.");
            }
            else if (permissions.Count == 0)
            {
                violations.Add($"{name}: no permission and not an allowlisted AllowAnonymous endpoint.");
            }

            foreach (var permission in permissions)
            {
                if (!source.Catalog.Contains(permission))
                    violations.Add($"{name}: permission '{permission}' is not in the catalog.");
                else if (!adminPermissions.Contains(permission))
                    violations.Add($"{name}: permission '{permission}' is not granted by the seed mapping to admin.");
            }
        }

        return violations;
    }

    /// <summary>The catalog and the admin seed must agree: admin holds every catalog permission.</summary>
    public static IReadOnlyList<string> AuditCatalog(StaticPermissionSource source) =>
        source.Catalog.Except(source.GetPermissions(["admin"]))
            .Select(p => $"Catalog permission '{p}' is not granted to admin.").ToList();
}
