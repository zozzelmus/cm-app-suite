using Conduct.Domain.Common;

namespace Conduct.Domain.Authorization;

public class GroupMembership : TenantedEntity
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AddedByUserId { get; set; }
}
