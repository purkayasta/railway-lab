using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Platform.Auth;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Endpoint metadata so tests can enumerate which permission each endpoint declares.</summary>
public sealed record RequiredPermissionMetadata(string Permission);

/// <summary>Succeeds when the union of the caller's role permissions contains the required permission.</summary>
public sealed class PermissionAuthorizationHandler(IPermissionSource source) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var roles = context.User.Claims
            .Where(c => c.Type is ClaimTypes.Role or AuthDefaults.RoleClaim)
            .Select(c => c.Value);
        if (source.GetPermissions(roles).Contains(requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Requires an authenticated caller holding the permission (401 without a token, 403 without the permission).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
        builder.WithMetadata(new RequiredPermissionMetadata(permission));
        return builder;
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The <c>sub</c> claim issued by the Auth service, used as the UserId everywhere.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : throw new InvalidOperationException("Token has no valid 'sub' claim.");
    }
}
