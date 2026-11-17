using Microsoft.AspNetCore.Identity;

namespace AuthService.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}
