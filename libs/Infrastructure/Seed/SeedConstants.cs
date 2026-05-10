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

    // ────────── Test users for nonprod ──────────
    // One LOB Manager + one Investigator per LOB, plus a global System Admin. Username ==
    // password (POC convenience, dev only). KeycloakSub GUIDs are stable so seed + KC import
    // + dev login-as widget all agree. Compliance Reviewer / LOB Admin personas covered by
    // sysadmin for now; add separate users if/when those role boundaries need exercising.
    public sealed record TestUserSpec(
        string Username,
        string KeycloakSub,
        string FirstName,
        string LastName,
        string RoleName,
        string? LobShortCode);  // null for Global-scope assignments (SysAdmin)

    public static readonly IReadOnlyList<TestUserSpec> TestUsers =
    [
        new("mgr-sui",       "00000000-0000-0000-0000-000000000020", "Mia",   "Manager-SUI",     RoleLobManager,    LobSpeakUpIntake),
        new("inv-sui",       "00000000-0000-0000-0000-000000000021", "Ian",   "Investigator-SUI",RoleInvestigator,  LobSpeakUpIntake),
        new("mgr-cmp",       "00000000-0000-0000-0000-000000000022", "Mara",  "Manager-CMP",     RoleLobManager,    LobCompliance),
        new("inv-cmp",       "00000000-0000-0000-0000-000000000023", "Igor",  "Investigator-CMP",RoleInvestigator,  LobCompliance),
        new("mgr-inv",       "00000000-0000-0000-0000-000000000024", "Mei",   "Manager-INV",     RoleLobManager,    LobInvestigations),
        new("inv-inv",       "00000000-0000-0000-0000-000000000025", "Ivo",   "Investigator-INV",RoleInvestigator,  LobInvestigations),
        new("mgr-inv-apac",  "00000000-0000-0000-0000-000000000026", "Mina",  "Manager-APAC",    RoleLobManager,    LobInvestigationsApac),
        new("inv-inv-apac",  "00000000-0000-0000-0000-000000000027", "Ines",  "Investigator-APAC",RoleInvestigator, LobInvestigationsApac),
        new("mgr-inv-in",    "00000000-0000-0000-0000-000000000028", "Maya",  "Manager-IN",      RoleLobManager,    LobInvestigationsIndia),
        new("inv-inv-in",    "00000000-0000-0000-0000-000000000029", "Ishan", "Investigator-IN", RoleInvestigator,  LobInvestigationsIndia),
        new("mgr-inv-ph",    "00000000-0000-0000-0000-000000000030", "Marcos","Manager-PH",      RoleLobManager,    LobInvestigationsPhilippines),
        new("inv-inv-ph",    "00000000-0000-0000-0000-000000000031", "Iris",  "Investigator-PH", RoleInvestigator,  LobInvestigationsPhilippines),
        new("mgr-hr-er",     "00000000-0000-0000-0000-000000000032", "Marta", "Manager-HR-ER",   RoleLobManager,    LobEmployeeRelations),
        new("inv-hr-er",     "00000000-0000-0000-0000-000000000033", "Ilia",  "Investigator-HR-ER",RoleInvestigator,LobEmployeeRelations),
        new("mgr-leg",       "00000000-0000-0000-0000-000000000034", "Milo",  "Manager-LEG",     RoleLobManager,    LobLegal),
        new("inv-leg",       "00000000-0000-0000-0000-000000000035", "Iona",  "Investigator-LEG",RoleInvestigator,  LobLegal),
        new("mgr-ia",        "00000000-0000-0000-0000-000000000036", "Mila",  "Manager-IA",      RoleLobManager,    LobInternalAudit),
        new("inv-ia",        "00000000-0000-0000-0000-000000000037", "Ilan",  "Investigator-IA", RoleInvestigator,  LobInternalAudit),
        new("sysadmin",      "00000000-0000-0000-0000-000000000038", "Sam",   "System-Admin",    RoleSystemAdmin,   null),
    ];
}
