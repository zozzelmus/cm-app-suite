using Conduct.Domain.Common;

namespace Conduct.Domain.Lobs;

// Line of Business — runtime-configurable, hierarchical (adjacency tree via ParentLobId).
// Visibility: membership on LOB X grants access to X + all descendants. Siblings invisible.
public class Lob : TenantedEntity
{
    public string Name { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;             // CaseNumber prefix component, unique per tenant
    public string? Description { get; set; }
    public Guid? ParentLobId { get; set; }                            // null = root LOB
    public Lob? Parent { get; set; }
    public ICollection<Lob> Children { get; set; } = new List<Lob>();

    // Approval quorum config (default AnyOneManager; admin-overridable)
    public ApprovalQuorum ApprovalQuorum { get; set; } = ApprovalQuorum.AnyOneManager;
    public int? QuorumNValue { get; set; }                            // for NofM: required count
    public Guid[] QuorumSpecificUserIds { get; set; } = Array.Empty<Guid>(); // for SpecificUsers
}

public enum ApprovalQuorum
{
    AnyOneManager = 0,
    AllManagers = 1,
    NofM = 2,
    SpecificUsers = 3,
}
