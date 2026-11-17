using AuthService.Data;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using Platform.Auth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Oidc;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // Sign out of every device: invalidates all refresh tokens and all browser SSO sessions of the caller.
        app.MapPost("/api/auth/sessions/revoke-all", RevokeAll).RequirePermission(Platform.Auth.Permissions.SessionsRevokeOwn);
        return app;
    }

    private static async Task<IResult> RevokeAll(
        HttpContext ctx, UserManager<AppUser> users,
        IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations)
    {
        var subject = ctx.User.GetClaim(Claims.Subject)!;
        var user = await users.FindByIdAsync(subject);
        if (user is null) return Results.Unauthorized();

        // 1. Every token and authorization of this subject becomes revoked: refresh tokens stop working, and so do
        //    access tokens as far as this service is concerned (token entry validation).
        await tokens.RevokeBySubjectAsync(subject);
        await authorizations.RevokeBySubjectAsync(subject);
        // 2. New security stamp: every SSO cookie issued before now is rejected, so no browser can silently sign back in.
        await users.UpdateSecurityStampAsync(user);
        return Results.NoContent();
    }
}
