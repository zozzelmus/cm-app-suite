using Conduct.Domain.Common;

namespace Conduct.Domain.CaseTypes;

// Runtime-configurable case type. Carries lifecycle (state machine), custom field schema (JSON Schema),
// per-role party data schemas, transfer rules, dashboard/display config, CaseNumber template.
// Cases reference CaseType.Id; type-specific behaviour is data-driven, never code-conditional.
public class CaseType : TenantedEntity
{
    public string Key { get; set; } = string.Empty;                   // stable id like "default" or "trade-surveillance"
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }                               // seeded vs admin-created
    public bool IsActive { get; set; } = true;

    // Schema version tag (mirrors $id in the schema doc); incremented on breaking change.
    public int SchemaVersion { get; set; } = 1;

    // JSON Schema (draft 2020-12) for Case.Data — validated server-side w/ JsonSchema.Net.
    public string FieldsSchemaJson { get; set; } = "{}";

    // { [roleOnCase]: JsonSchema } for CaseParty.Data — keyed by RoleOnCase enum string.
    public string PartyDataSchemasJson { get; set; } = "{}";

    // Lifecycle config (states + transitions + guards + autoroute hooks). JSON.
    public string LifecycleJson { get; set; } = "{}";

    // Transfer rules: per source-LOB allowed targets + ApprovalRequirement per rule. JSON.
    public string TransferRulesJson { get; set; } = "{}";

    // CaseNumber template (default `{year}-{lobCode}-{seq:000000}`).
    public string NumberFormat { get; set; } = "{year}-{lobCode}-{seq:000000}";

    // Notes edit boundary defaults to "lock on case Closed". CaseType can override (per-state-name freeze list).
    public string NotesLifecyclePolicyJson { get; set; } = "{\"freezeOnStates\":[\"Closed\"]}";
}
