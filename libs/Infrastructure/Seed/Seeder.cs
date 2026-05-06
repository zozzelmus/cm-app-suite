using Conduct.Domain.Authorization;
using Conduct.Domain.CaseTypes;
using Conduct.Domain.Identity;
using Conduct.Domain.Lobs;
using Conduct.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using static Conduct.Infrastructure.Seed.SeedConstants;

namespace Conduct.Infrastructure.Seed;

// Idempotent reference-data seeder. Each step detects pre-existing rows by stable keys
// (Lob.ShortCode, CaseType.Key, Role.Name, User.KeycloakSub, EmployeeProfile.PartyId)
// before inserting. Safe to run on every dev startup; runs only once meaningfully.
public sealed class Seeder(ConductDbContext db)
{
    // Stable advisory-lock key for the seed routine. Postgres advisory locks are tx-scoped;
    // a second concurrent caller blocks until the first commits, so racing API instances
    // don't both see "missing" and double-insert.
    private const long SeedAdvisoryLockKey = 0x_C0_4D_5C_7_5E_ED_01_01;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Schema lifecycle is the caller's responsibility (Program.cs runs MigrateAsync).
        // Tests use EnsureCreatedAsync via the fixture. Don't double-up here.
        //
        // Aspire's Npgsql integration applies NpgsqlRetryingExecutionStrategy by default,
        // which forbids user-initiated transactions unless wrapped in the strategy's ExecuteAsync.
        // Wrapping here keeps both retry-enabled (Aspire) and retry-disabled (test) DbContexts happy.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async cancellationToken =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                new object[] { SeedAdvisoryLockKey },
                cancellationToken);

            await SeedLobsAsync(cancellationToken);
            await SeedDefaultCaseTypeAsync(cancellationToken);
            await SeedBuiltInRolesAsync(cancellationToken);
            var (demoUserId, _) = await SeedDemoUserAndPartyAsync(cancellationToken);
            await SeedDemoAssignmentAsync(demoUserId, cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }, ct);
    }

    // ────────── LOBs ──────────

    private async Task SeedLobsAsync(CancellationToken ct)
    {
        var existing = await db.Lobs
            .Where(x => x.TenantId == DemoTenantId)
            .ToDictionaryAsync(x => x.ShortCode, ct);

        Lob addIfMissing(string code, string name, Guid? parentId, string? description = null)
        {
            if (existing.TryGetValue(code, out var found)) return found;
            var l = new Lob
            {
                TenantId = DemoTenantId,
                Name = name,
                ShortCode = code,
                Description = description,
                ParentLobId = parentId,
                ApprovalQuorum = ApprovalQuorum.AnyOneManager,
            };
            db.Lobs.Add(l);
            existing[code] = l;
            return l;
        }

        addIfMissing(LobSpeakUpIntake, "Speak-Up Intake", null);
        addIfMissing(LobCompliance, "Compliance", null);
        var inv = addIfMissing(LobInvestigations, "Investigations", null);
        addIfMissing(LobEmployeeRelations, "Employee Relations", null);
        addIfMissing(LobLegal, "Legal", null);
        addIfMissing(LobInternalAudit, "Internal Audit", null);

        // Save the root LOBs so children can reference INV.Id (children need parent's PK populated).
        await db.SaveChangesAsync(ct);

        addIfMissing(LobInvestigationsApac, "Investigations APAC", inv.Id);
        addIfMissing(LobInvestigationsIndia, "Investigations India", inv.Id);
        addIfMissing(LobInvestigationsPhilippines, "Investigations Philippines", inv.Id);

        await db.SaveChangesAsync(ct);
    }

    // ────────── Default CaseType ──────────

    private async Task SeedDefaultCaseTypeAsync(CancellationToken ct)
    {
        var existing = await db.CaseTypes.SingleOrDefaultAsync(
            x => x.TenantId == DemoTenantId && x.Key == DefaultCaseTypeKey, ct);
        if (existing is not null) return;

        db.CaseTypes.Add(new CaseType
        {
            TenantId = DemoTenantId,
            Key = DefaultCaseTypeKey,
            Name = "Default",
            Description = "Generic conduct case template — edit to suit specific processes (or clone for new types).",
            IsBuiltIn = true,
            IsActive = true,
            SchemaVersion = 1,
            FieldsSchemaJson = DefaultCaseTypeFieldsSchemaJson,
            PartyDataSchemasJson = "{}",
            LifecycleJson = DefaultCaseTypeLifecycleJson,
            TransferRulesJson = "{}",
            NumberFormat = "{year}-{lobCode}-{seq:000000}",
            NotesLifecyclePolicyJson = """{"freezeOnStates":["Closed"]}""",
        });

        await db.SaveChangesAsync(ct);
    }

    // ────────── Built-in Roles ──────────

    private async Task SeedBuiltInRolesAsync(CancellationToken ct)
    {
        var existing = await db.Roles
            .Where(x => x.TenantId == DemoTenantId)
            .ToDictionaryAsync(x => x.Name, ct);

        void addIfMissing(string name, string description, string[] permissions)
        {
            if (existing.ContainsKey(name)) return;
            db.Roles.Add(new Role
            {
                TenantId = DemoTenantId,
                Name = name,
                Description = description,
                IsBuiltIn = true,
                Permissions = permissions,
            });
        }

        addIfMissing(RoleInvestigator, "Front-line investigator on cases.", [
            Permissions.CaseRead,
            Permissions.CaseCreate,
            Permissions.CaseUpdate,
            Permissions.CaseTransferInitiate,
            Permissions.NoteWrite,
        ]);

        addIfMissing(RoleLobManager, "LOB-level manager; approves cross-LOB transfers and closures.", [
            Permissions.CaseRead,
            Permissions.CaseCreate,
            Permissions.CaseUpdate,
            Permissions.CaseTransferInitiate,
            Permissions.CaseClose,
            Permissions.NoteWrite,
            Permissions.TaskApproveLobManager,
        ]);

        addIfMissing(RoleLobAdmin, "LOB administrator; manages memberships in addition to manager privileges.", [
            Permissions.CaseRead,
            Permissions.CaseCreate,
            Permissions.CaseUpdate,
            Permissions.CaseTransferInitiate,
            Permissions.CaseClose,
            Permissions.NoteWrite,
            Permissions.TaskApproveLobManager,
            Permissions.LobMembershipManage,
        ]);

        addIfMissing(RoleComplianceReviewer, "Read + update access for compliance review work.", [
            Permissions.CaseRead,
            Permissions.CaseUpdate,
            Permissions.AuditView,
        ]);

        addIfMissing(RoleSystemAdmin, "Tenant-wide system administrator.", [
            Permissions.SystemAdmin,
            Permissions.LobAdmin,
            Permissions.LobMembershipManage,
            Permissions.CaseTypeAdmin,
            Permissions.RoleAdmin,
            Permissions.AssignmentManage,
            Permissions.AuditView,
            Permissions.AuditExport,
            Permissions.CaseReopen,
        ]);

        await db.SaveChangesAsync(ct);
    }

    // ────────── Demo User + Party + EmployeeProfile ──────────

    private async Task<(Guid userId, Guid partyId)> SeedDemoUserAndPartyAsync(CancellationToken ct)
    {
        var existingUser = await db.Users.SingleOrDefaultAsync(
            x => x.TenantId == DemoTenantId && x.KeycloakSub == DemoUserKeycloakSub, ct);
        if (existingUser is not null && existingUser.PartyId is not null)
            return (existingUser.Id, existingUser.PartyId.Value);

        Party party;
        if (existingUser is { PartyId: not null })
        {
            party = await db.Parties.SingleAsync(x => x.Id == existingUser.PartyId, ct);
        }
        else
        {
            party = new Party
            {
                TenantId = DemoTenantId,
                IdentityKind = IdentityKind.Employee,
                DisplayName = "Demo User",
                IsAnonymous = false,
            };
            db.Parties.Add(party);
            await db.SaveChangesAsync(ct);
        }

        var existingProfile = await db.EmployeeProfiles.SingleOrDefaultAsync(p => p.PartyId == party.Id, ct);
        if (existingProfile is null)
        {
            db.EmployeeProfiles.Add(new EmployeeProfile
            {
                TenantId = DemoTenantId,
                PartyId = party.Id,
                EmployeeId = "DEMO-0001",
                Department = "Investigations",
                JobTitle = "Investigator",
                Email = DemoUserEmail,
                SourceSystemRef = "seed:demo",
            });
        }

        if (existingUser is null)
        {
            var user = new User
            {
                TenantId = DemoTenantId,
                KeycloakSub = DemoUserKeycloakSub,
                Username = DemoUsername,
                Email = DemoUserEmail,
                FirstName = "Demo",
                LastName = "User",
                PartyId = party.Id,
                IsActive = true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return (user.Id, party.Id);
        }
        else
        {
            existingUser.PartyId = party.Id;
            await db.SaveChangesAsync(ct);
            return (existingUser.Id, party.Id);
        }
    }

    // ────────── Demo Assignment (Investigator on Investigations APAC) ──────────

    private async Task SeedDemoAssignmentAsync(Guid demoUserId, CancellationToken ct)
    {
        var role = await db.Roles.SingleAsync(
            x => x.TenantId == DemoTenantId && x.Name == RoleInvestigator, ct);
        var lob = await db.Lobs.SingleAsync(
            x => x.TenantId == DemoTenantId && x.ShortCode == LobInvestigationsApac, ct);

        var existing = await db.Assignments.SingleOrDefaultAsync(a =>
            a.TenantId == DemoTenantId &&
            a.SubjectType == AssignmentSubjectType.User &&
            a.SubjectId == demoUserId &&
            a.RoleId == role.Id &&
            a.ScopeType == AssignmentScopeType.Lob &&
            a.ScopeId == lob.Id,
            ct);

        if (existing is not null) return;

        db.Assignments.Add(new Assignment
        {
            TenantId = DemoTenantId,
            SubjectType = AssignmentSubjectType.User,
            SubjectId = demoUserId,
            RoleId = role.Id,
            ScopeType = AssignmentScopeType.Lob,
            ScopeId = lob.Id,
        });

        await db.SaveChangesAsync(ct);
    }

    // ────────── JSON literals (kept inline so seed content is reviewable in one place) ──────────

    // $id intentionally omitted: schema version is tracked on CaseType.SchemaVersion column,
    // and avoiding $id keeps JsonSchema.Net's process-wide SchemaRegistry from rejecting
    // re-parses (same $id under different instances throws). Add $id only if cross-schema $ref'ing is needed.
    private const string DefaultCaseTypeFieldsSchemaJson = /* lang=json */ """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "title": "Default conduct case",
      "additionalProperties": false,
      "properties": {
        "summary": {
          "type": "string",
          "minLength": 1,
          "maxLength": 5000,
          "x-ui:control": "textarea",
          "x-ui:order": 10,
          "x-ui:label": "Summary",
          "x-ui:help": "Concise description of the conduct concern."
        },
        "occurredAt": {
          "type": "string",
          "format": "date-time",
          "x-ui:control": "datetime",
          "x-ui:order": 20,
          "x-ui:label": "Occurred at"
        },
        "severity": {
          "type": "string",
          "enum": ["Low", "Medium", "High", "Critical"],
          "x-ui:control": "select",
          "x-ui:order": 30,
          "x-ui:label": "Severity"
        }
      },
      "required": ["summary"]
    }
    """;

    private const string DefaultCaseTypeLifecycleJson = /* lang=json */ """
    {
      "states": [
        { "name": "Open",            "isTerminal": false },
        { "name": "Triaged",         "isTerminal": false },
        { "name": "Investigating",   "isTerminal": false },
        { "name": "PendingDecision", "isTerminal": false },
        { "name": "Closed",          "isTerminal": true  }
      ],
      "transitions": [
        { "from": "Open",            "to": "Triaged"         },
        { "from": "Open",            "to": "Closed"          },
        { "from": "Triaged",         "to": "Investigating"   },
        { "from": "Triaged",         "to": "Closed"          },
        { "from": "Investigating",   "to": "PendingDecision" },
        { "from": "PendingDecision", "to": "Closed"          },
        { "from": "Closed",          "to": "Investigating", "requiresPermission": "case.reopen" }
      ],
      "closureReasons": [
        "Substantiated",
        "Unsubstantiated",
        "NoActionWarranted",
        "Withdrawn",
        "Duplicate",
        "OutOfScope"
      ]
    }
    """;
}
