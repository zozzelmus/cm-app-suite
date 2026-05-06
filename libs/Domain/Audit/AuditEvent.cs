using Conduct.Domain.Common;

namespace Conduct.Domain.Audit;

// Append-only audit row. EF SaveChangesInterceptor auto-captures entity Create/Update/Delete;
// service code emits explicit domain events (StateTransition, Transfer, TaskApprove, etc.) for richness.
// Postgres role grants only INSERT on this table; trigger blocks UPDATE/DELETE belt-and-suspenders.
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "system";                     // UserId.ToString() | "system" | "autoroute-rule:{id}"
    public string EntityType { get; set; } = string.Empty;            // e.g., "Case", "CaseNote", "Lob", "Assignment"
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;                // Create | Update | Delete | StateTransition | Transfer | TaskApprove | ...
    public string ChangeSetJson { get; set; } = "{}";                 // before/after diff for Update; full snapshot for Create/Delete
    public string ContextJson { get; set; } = "{}";                   // CaseId, TaskId, RuleId, RequestCorrelationId, IP, ...
}
