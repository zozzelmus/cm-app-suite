using Conduct.Domain.Common;

namespace Conduct.Domain.Authorization;

// Runtime-configurable bundle of permissions. Admin CRUD'd. References permission keys (constants in Permissions class).
public class Role : TenantedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }                               // seeded vs admin-created
    public string[] Permissions { get; set; } = Array.Empty<string>(); // permission keys; see Permissions constants
}
