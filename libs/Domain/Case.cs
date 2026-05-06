namespace Conduct.Domain;

// PLACEHOLDER — real Case model lands once domain grilling completes (LOBs, typology, lifecycle, transfer protocol).
// TenantId present for multi-tenant readiness.
public class Case
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
