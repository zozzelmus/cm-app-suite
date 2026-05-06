using Conduct.Domain.Common;

namespace Conduct.Domain.Parties;

// Universal person record. Reusable across cases (one Party row, many CaseParty rows linking it).
// Per-kind fields live on the appropriate Profile table (1:1 w/ Party where applicable).
public class Party : TenantedEntity
{
    public IdentityKind IdentityKind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }                             // true for whistleblower-class reporters
}
