using System.Text.Json;

namespace Platform.Auth;

/// <summary>Resolves the effective permissions for a set of role names. Unknown roles grant nothing.</summary>
public interface IPermissionSource
{
    IReadOnlySet<string> GetPermissions(IEnumerable<string> roles);
}

/// <summary>
/// Reads the role-to-permission mapping from a JSON file (deploy/auth/default-role-permissions.json).
/// Used until the Auth service exists (part B) and in tests.
/// </summary>
public sealed class StaticPermissionSource : IPermissionSource
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _rolePermissions;

    public IReadOnlySet<string> Catalog { get; }

    public StaticPermissionSource(string json)
    {
        var file = JsonSerializer.Deserialize<MappingFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Role permission file is empty.");

        Catalog = file.Permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (role, permissions) in file.Roles)
            foreach (var permission in permissions.Where(p => !Catalog.Contains(p)))
                throw new InvalidOperationException($"Role '{role}' references permission '{permission}' that is not in the catalog.");

        _rolePermissions = file.Roles.ToDictionary(
            r => r.Key,
            r => (IReadOnlySet<string>)r.Value.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    public static StaticPermissionSource FromFile(string path) => new(File.ReadAllText(path));

    public IReadOnlySet<string> GetPermissions(IEnumerable<string> roles)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
            if (_rolePermissions.TryGetValue(role, out var permissions))
                result.UnionWith(permissions);
        return result;
    }

    private sealed record MappingFile(List<CatalogEntry> Permissions, Dictionary<string, List<string>> Roles);
    private sealed record CatalogEntry(string Name, string Service, string Description);
}
