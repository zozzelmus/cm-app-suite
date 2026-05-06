using Conduct.Domain.Common;

namespace Conduct.Domain.Cases;

// Core conduct case. Type-specific data lives in Data jsonb governed by CaseType.FieldsSchema.
// Status is a string carrying the state name from CaseType.Lifecycle (NOT an enum — per-type state graphs).
public class Case : TenantedEntity
{
    public Guid CaseTypeId { get; set; }
    public Guid OwnerLobId { get; set; }                              // exactly one owner LOB at a time (transfer = change owner)
    public string CaseNumber { get; set; } = string.Empty;            // human handle: "2026-INV-APAC-000042" — immutable
    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Open";                      // state name from CaseType.Lifecycle
    public string? ClosureReason { get; set; }                        // set only when status is a terminal/Closed state

    public string DataJson { get; set; } = "{}";                      // payload validated against CaseType.FieldsSchema
    public int SchemaVersion { get; set; } = 1;                       // CaseType schema version this case was authored against

    public string ExternalRefsJson { get; set; } = "{}";              // surveillance alert id, hotline ref, regulator id, ...

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? LegalHoldUntil { get; set; }               // cascades to Evidence; suppresses retention deletion
    public bool IsImported { get; set; }                              // legacy bulk import marker (skips sequence advance)
}
