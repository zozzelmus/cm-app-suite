using Conduct.Domain.Common;

namespace Conduct.Domain.Cases;

// Join row tying a Party to a Case w/ a RoleOnCase. Carries case-specific context in Data jsonb.
public class CaseParty : TenantedEntity
{
    public Guid CaseId { get; set; }
    public Guid PartyId { get; set; }
    public string RoleOnCase { get; set; } = Cases.RoleOnCase.Other;  // stored as string; see RoleOnCase constants
    public bool IsAnonymousOnThisCase { get; set; }                   // separate from Party.IsAnonymous (per-case stance)
    public string DataJson { get; set; } = "{}";                      // role+CaseType-specific schema; validated server-side
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AddedByUserId { get; set; }
}
