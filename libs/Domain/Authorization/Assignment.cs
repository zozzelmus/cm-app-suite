using Conduct.Domain.Common;

namespace Conduct.Domain.Authorization;

// Grants a Role to a Subject (User or Group), scoped to Global | Lob | CaseType.
// Effective permissions for a User = union over (assignments where Subject is User OR Group containing User),
// further filtered by ScopeType matching the request context (LOB ancestor walk for Lob scope).
public class Assignment : TenantedEntity
{
    public AssignmentSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }                               // UserId or GroupId
    public Guid RoleId { get; set; }
    public AssignmentScopeType ScopeType { get; set; } = AssignmentScopeType.Global;
    public Guid? ScopeId { get; set; }                                // null for Global; LobId or CaseTypeId otherwise
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? GrantedByUserId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }                    // optional time-boxed grant
}

public enum AssignmentSubjectType { User = 0, Group = 1 }
public enum AssignmentScopeType   { Global = 0, Lob = 1, CaseType = 2 }
