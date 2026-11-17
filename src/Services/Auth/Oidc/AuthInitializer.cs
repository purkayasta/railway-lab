using AuthService.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Oidc;

/// <summary>
/// Runs before the service accepts requests: applies migrations, then makes sure roles, the demo users and the OAuth clients
/// exist and match configuration. Every step is idempotent, so a restart never duplicates users; client settings are
/// intentionally configuration-driven and are re-applied.
/// </summary>
public sealed class AuthInitializer(IServiceProvider services, IConfiguration configuration, ILogger<AuthInitializer> log) : IHostedService
{
    private static readonly string[] Roles = ["passenger", "station-agent", "operations", "admin"];

    private static readonly (string UserName, string DisplayName, string Role)[] DemoUsers =
    [
        ("alice", "Alice Passenger", "passenger"),
        ("bob", "Bob Passenger", "passenger"),
        ("sam", "Sam Agent", "station-agent"),
        ("olivia", "Olivia Operations", "operations"),
        ("admin", "Ada Admin", "admin"),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (db.Database.IsSqlServer()) await db.Database.MigrateAsync(ct);
        else await db.Database.EnsureCreatedAsync(ct);

        await SeedRolesAndUsers(scope.ServiceProvider);
        await SeedClients(scope.ServiceProvider, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedRolesAndUsers(IServiceProvider sp)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles)
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole<Guid>(role)), $"create role {role}");

        var password = configuration["Auth:SeedUserPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            log.LogInformation("Auth:SeedUserPassword is empty; demo users are not seeded.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        foreach (var (userName, displayName, role) in DemoUsers)
        {
            if (await users.FindByNameAsync(userName) is not null) continue;
            var user = new AppUser { UserName = userName, Email = $"{userName}@railway.test", EmailConfirmed = true, DisplayName = displayName };
            Check(await users.CreateAsync(user, password), $"create user {userName}");
            Check(await users.AddToRoleAsync(user, role), $"assign {role} to {userName}");
        }
    }

    private async Task SeedClients(IServiceProvider sp, CancellationToken ct)
    {
        var apps = sp.GetRequiredService<IOpenIddictApplicationManager>();
        var webOrigin = configuration["Auth:WebOrigin"] ?? "http://localhost:4200";

        await Upsert(apps, new OpenIddictApplicationDescriptor
        {
            ClientId = "web",
            DisplayName = "Railway web app",
            ClientType = ClientTypes.Public, // a browser cannot keep a secret; PKCE protects the code instead
            ConsentType = ConsentTypes.Implicit,
            RedirectUris = { new Uri($"{webOrigin}/callback") },
            PostLogoutRedirectUris = { new Uri($"{webOrigin}/") },
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession, Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email, Permissions.Scopes.Profile, Permissions.Scopes.Roles,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        }, ct);

        var internalSecret = configuration["Auth:InternalServicesClientSecret"];
        if (string.IsNullOrWhiteSpace(internalSecret))
        {
            log.LogInformation("Auth:InternalServicesClientSecret is empty; the internal-services client is not registered.");
            return;
        }

        await Upsert(apps, new OpenIddictApplicationDescriptor
        {
            ClientId = "internal-services",
            ClientSecret = internalSecret,
            DisplayName = "Internal services (machine to machine)",
            ClientType = ClientTypes.Confidential,
            Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials },
        }, ct);
    }

    private static async Task Upsert(IOpenIddictApplicationManager apps, OpenIddictApplicationDescriptor descriptor, CancellationToken ct)
    {
        var existing = await apps.FindByClientIdAsync(descriptor.ClientId!, ct);
        if (existing is null) await apps.CreateAsync(descriptor, ct);
        else await apps.UpdateAsync(existing, descriptor, ct);
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {action}: {string.Join("; ", result.Errors.Select(e => e.Code))}");
    }
}
