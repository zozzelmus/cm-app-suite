using Conduct.Domain.Common;

namespace Conduct.Domain.Authorization;

// User collection for bulk role assignment.
public class Group : TenantedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
