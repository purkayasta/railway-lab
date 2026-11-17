using System.Collections.Immutable;
using System.Security.Claims;
using AuthService.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Oidc;

/// <summary>
/// The OAuth 2.0 / OpenID Connect endpoints. OpenIddict parses and validates every protocol message (PKCE, redirect URIs,
/// client authentication, token formats); these handlers only decide who the user is and which claims go into the tokens.
/// </summary>
public static class ConnectEndpoints
{
    public static IEndpointRouteBuilder MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        // Browser redirects here from the SPA. Authenticated by the SSO cookie, not by a bearer token.
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], Authorize).AllowAnonymous();
        // Called by clients with a code, refresh token or client credentials.
        app.MapPost("/connect/token", Exchange).AllowAnonymous();
        app.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], UserInfo).AllowAnonymous();
        app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], Logout).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> Authorize(
        HttpContext ctx, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        IOpenIddictAuthorizationManager authorizations, IOpenIddictApplicationManager applications)
    {
        var request = ctx.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OpenID Connect request missing.");
        var cookie = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal!) : null;

        if (user is null || request.HasPromptValue(PromptValues.Login))
        {
            if (request.HasPromptValue(PromptValues.None))
                return Error(Errors.LoginRequired, "The user is not signed in.");

            // Send the browser to the login page and bring it back to this exact request, minus prompt=login.
            var parameters = ctx.Request.HasFormContentType ? ctx.Request.Form.ToList() : ctx.Request.Query.ToList();
            parameters.RemoveAll(p => p.Key == Parameters.Prompt);
            var returnUrl = ctx.Request.PathBase + ctx.Request.Path + QueryString.Create(parameters);
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [IdentityConstants.ApplicationScheme]);
        }

        if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            return Error(Errors.AccessDenied, "The account cannot sign in.");

        var application = await applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("The client was validated by OpenIddict and must exist.");

        var scopes = request.GetScopes();
        var principal = await CreateUserPrincipal(user, users, scopes);

        // A permanent authorization groups every token this user was given for this client on this device. Revoking
        // it (or the user's whole subject) kills the refresh tokens in one step.
        var authorization = await authorizations.CreateAsync(
            principal, await users.GetUserIdAsync(user), (await applications.GetIdAsync(application))!,
            AuthorizationTypes.Permanent, principal.GetScopes());
        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization));

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Exchange(HttpContext ctx, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var request = ctx.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OpenID Connect request missing.");

        if (request.IsClientCredentialsGrantType())
        {
            // Machine to machine: the client itself is the subject and carries the "service" role.
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId);
            identity.SetClaim(Claims.Name, request.ClientId);
            identity.SetClaim(Claims.Role, "service");
            var service = new ClaimsPrincipal(identity);
            service.SetScopes(request.GetScopes());
            service.SetResources(AuthConstants.Audience);
            service.SetDestinations(_ => [Destinations.AccessToken]);
            return Results.SignIn(service, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // OpenIddict has already validated the code or refresh token (and consumed it: refresh tokens rotate).
            var redeemed = (await ctx.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;
            var user = await users.FindByIdAsync(redeemed.GetClaim(Claims.Subject)!);
            if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
                return Error(Errors.InvalidGrant, "The user is no longer allowed to sign in.");

            // Claims are rebuilt from the database on every exchange, so role changes arrive with the next refresh.
            var principal = await CreateUserPrincipal(user, users, redeemed.GetScopes());
            principal.SetAuthorizationId(redeemed.GetAuthorizationId());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private static async Task<IResult> UserInfo(HttpContext ctx, UserManager<AppUser> users)
    {
        var token = (await ctx.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var user = token is null ? null : await users.FindByIdAsync(token.GetClaim(Claims.Subject)!);
        if (token is null || user is null)
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The access token is no longer valid.",
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        var claims = new Dictionary<string, object> { [Claims.Subject] = await users.GetUserIdAsync(user) };
        if (token.HasScope(Scopes.Profile))
        {
            claims[Claims.PreferredUsername] = user.UserName!;
            if (user.DisplayName is not null) claims[Claims.Name] = user.DisplayName;
        }
        if (token.HasScope(Scopes.Email)) claims[Claims.Email] = user.Email!;
        if (token.HasScope(Scopes.Roles)) claims[Claims.Role] = (await users.GetRolesAsync(user)).ToArray();
        return Results.Ok(claims);
    }

    private static async Task<IResult> Logout(HttpContext ctx, SignInManager<AppUser> signIn)
    {
        // RP-initiated logout (OIDC): clear the SSO cookie, then let OpenIddict redirect to a registered post_logout_redirect_uri.
        await signIn.SignOutAsync();
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<ClaimsPrincipal> CreateUserPrincipal(AppUser user, UserManager<AppUser> users, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.PreferredUsername, Claims.Role);
        identity.SetClaim(Claims.Subject, await users.GetUserIdAsync(user))
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetResources(AuthConstants.Audience); // becomes "aud": the token is meant for this API
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    // Decide which token carries which claim. Access tokens get everything the API needs; the ID token (read by the SPA)
    // only gets what the granted scopes allow.
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
            case Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;
            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Roles)) yield return Destinations.IdentityToken;
                yield break;
            case Claims.Name when claim.Subject!.HasScope(Scopes.Profile):
            case Claims.Email when claim.Subject!.HasScope(Scopes.Email):
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;
        }
    }

    private static IResult Error(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
