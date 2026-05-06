namespace Conduct.Infrastructure.Seed;

// Stable seed identifiers — kept as constants so migrations + tests + seed code agree.
public static class SeedConstants
{
    // Single demo tenant for POC. Multi-tenant resolver is post-MVP.
    public static readonly Guid DemoTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // CaseType keys
    public const string DefaultCaseTypeKey = "default";

    // Built-in role names
    public const string RoleInvestigator = "Investigator";
    public const string RoleLobManager = "LOB Manager";
    public const string RoleLobAdmin = "LOB Admin";
    public const string RoleComplianceReviewer = "Compliance Reviewer";
    public const string RoleSystemAdmin = "System Admin";

    // LOB short codes (keys for idempotent seed)
    public const string LobSpeakUpIntake = "SUI";
    public const string LobCompliance = "CMP";
    public const string LobInvestigations = "INV";
    public const string LobInvestigationsApac = "INV-APAC";
    public const string LobInvestigationsIndia = "INV-IN";
    public const string LobInvestigationsPhilippines = "INV-PH";
    public const string LobEmployeeRelations = "HR-ER";
    public const string LobLegal = "LEG";
    public const string LobInternalAudit = "IA";

    // Demo user (matches infra/keycloak/realm/conduct-realm.json)
    public const string DemoUserKeycloakSub = "00000000-0000-0000-0000-000000000010";
    public const string DemoUsername = "demo";
    public const string DemoUserEmail = "demo@conduct.local";
}
