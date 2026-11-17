using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

/// <summary>Users and roles (ASP.NET Core Identity) plus OpenIddict applications, authorizations, scopes and tokens, all in schema <c>auth</c>.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "auth";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
    }
}
