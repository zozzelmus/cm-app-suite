namespace Conduct.Domain.Common;

// Base for all multi-tenant entities. TenantId is the hard isolation boundary enforced by Postgres RLS.
// Audit columns track creation; mutation history lives in AuditEvent (not on the row).
public abstract class TenantedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }                    // soft-delete marker (filtered globally by EF)
}
