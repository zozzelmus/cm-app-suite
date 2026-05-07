using AwesomeAssertions;
using Conduct.Domain.Cases;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Conduct.Api.Tests.IntakeConsumer;

[Collection("postgres")]
public class IntakeProcessorTests(Conduct.Api.Tests.Seed.PostgresFixture pg)
{
    private static CreateCaseCommand BuildCommand(Guid receiptId, Guid tenantId, string lobShortCode) => new()
    {
        ReceiptId = receiptId,
        TenantId = tenantId,
        CaseTypeKey = SeedConstants.DefaultCaseTypeKey,
        LobShortCode = lobShortCode,
        Title = "Suspected violation",
        SchemaVersion = 1,
        DataJson = """{"summary":"Test summary","severity":"High"}""",
        OccurredAt = new DateTimeOffset(2026, 4, 15, 14, 0, 0, TimeSpan.Zero),
    };

    private static (IntakeProcessor svc, ConductDbContext db, FixedTenantContext tc, CaseAllocator alloc) BuildSubject(
        ConductDbContext db, Guid tenantId)
    {
        var tc = new FixedTenantContext(tenantId);
        var alloc = new CaseAllocator(db);
        var svc = new IntakeProcessor(db, alloc, tc, NullLogger<IntakeProcessor>.Instance);
        return (svc, db, tc, alloc);
    }

    private static async Task SetTenantOnDbAsync(ConductDbContext db, Guid tenant)
    {
        // Open the EF context's connection up-front and pin it so subsequent EF commands reuse it.
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SET app.tenant_id = '{tenant:D}'";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ProcessAsync_validCommand_writesCase_partyAndAuditEvent_updatesReceipt()
    {
        // Use the migrated DB (RLS enforced) so tenant scoping is real
        var (db, _) = await pg.CreateFreshMigratedDbAsync();
        await using var _db = db;

        var tenant = SeedConstants.DemoTenantId;
        await SetTenantOnDbAsync(db, tenant);
        await new Seeder(db).SeedAsync();

        // Pre-create the receipt (F4 would have done this in production)
        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant, Status = IntakeStatus.Queued,
            CaseTypeKey = SeedConstants.DefaultCaseTypeKey, LobShortCode = SeedConstants.LobInvestigationsApac,
        });
        await db.SaveChangesAsync();

        var (svc, _, _, _) = BuildSubject(db, tenant);
        var outcome = await svc.ProcessAsync(BuildCommand(receiptId, tenant, SeedConstants.LobInvestigationsApac), default);

        outcome.Processed.Should().BeTrue();
        outcome.CaseId.Should().NotBeNull();
        outcome.CaseNumber.Should().NotBeNullOrEmpty();
        outcome.CaseNumber.Should().StartWith("2026-INV-APAC-");

        var c = await db.Cases.SingleAsync(x => x.Id == outcome.CaseId);
        c.OwnerLobId.Should().NotBeEmpty();
        c.Status.Should().Be("Open");
        c.CaseTypeId.Should().NotBeEmpty();

        var receipt = await db.CaseIntakes.SingleAsync(x => x.Id == receiptId);
        receipt.Status.Should().Be(IntakeStatus.Completed);
        receipt.CaseId.Should().Be(c.Id);
        receipt.CaseNumber.Should().Be(outcome.CaseNumber);

        var auditEvents = await db.AuditEvents.AsNoTracking().ToListAsync();
        auditEvents.Should().Contain(e => e.Action == "CaseCreated" && e.EntityId == c.Id);
    }

    [Fact]
    public async Task ProcessAsync_idempotent_reDelivery_returnsSameCase_doesNotDoubleWrite()
    {
        var (db, _) = await pg.CreateFreshMigratedDbAsync();
        await using var _db = db;

        var tenant = SeedConstants.DemoTenantId;
        await SetTenantOnDbAsync(db, tenant);
        await new Seeder(db).SeedAsync();

        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant, Status = IntakeStatus.Queued,
            CaseTypeKey = SeedConstants.DefaultCaseTypeKey, LobShortCode = SeedConstants.LobInvestigationsApac,
        });
        await db.SaveChangesAsync();

        var (svc, _, _, _) = BuildSubject(db, tenant);
        var first = await svc.ProcessAsync(BuildCommand(receiptId, tenant, SeedConstants.LobInvestigationsApac), default);
        var second = await svc.ProcessAsync(BuildCommand(receiptId, tenant, SeedConstants.LobInvestigationsApac), default);

        first.Processed.Should().BeTrue();
        second.Processed.Should().BeTrue();
        second.CaseId.Should().Be(first.CaseId);
        second.CaseNumber.Should().Be(first.CaseNumber);

        // Only one Case row created
        (await db.Cases.CountAsync(x => x.Id == first.CaseId)).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_allocates_sequential_case_numbers_for_same_lob()
    {
        var (db, _) = await pg.CreateFreshMigratedDbAsync();
        await using var _db = db;

        var tenant = SeedConstants.DemoTenantId;
        await SetTenantOnDbAsync(db, tenant);
        await new Seeder(db).SeedAsync();

        var (svc, _, _, _) = BuildSubject(db, tenant);
        var numbers = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var receiptId = Guid.NewGuid();
            db.CaseIntakes.Add(new CaseIntake
            {
                Id = receiptId, TenantId = tenant, Status = IntakeStatus.Queued,
                CaseTypeKey = SeedConstants.DefaultCaseTypeKey, LobShortCode = SeedConstants.LobInvestigationsApac,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var outcome = await svc.ProcessAsync(BuildCommand(receiptId, tenant, SeedConstants.LobInvestigationsApac), default);
            numbers.Add(outcome.CaseNumber!);
        }

        numbers.Should().Equal(
            "2026-INV-APAC-000001",
            "2026-INV-APAC-000002",
            "2026-INV-APAC-000003"
        );
    }

    [Fact]
    public async Task ProcessAsync_unknown_caseType_marks_receipt_failed()
    {
        var (db, _) = await pg.CreateFreshMigratedDbAsync();
        await using var _db = db;

        var tenant = SeedConstants.DemoTenantId;
        await SetTenantOnDbAsync(db, tenant);
        await new Seeder(db).SeedAsync();

        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant, Status = IntakeStatus.Queued,
            CaseTypeKey = "no-such-type", LobShortCode = SeedConstants.LobInvestigationsApac,
        });
        await db.SaveChangesAsync();

        var (svc, _, _, _) = BuildSubject(db, tenant);
        var baseCmd = BuildCommand(receiptId, tenant, SeedConstants.LobInvestigationsApac);
        var cmd = new CreateCaseCommand
        {
            ReceiptId = baseCmd.ReceiptId,
            TenantId = baseCmd.TenantId,
            CaseTypeKey = "no-such-type",
            LobShortCode = baseCmd.LobShortCode,
            Title = baseCmd.Title,
            SchemaVersion = baseCmd.SchemaVersion,
            DataJson = baseCmd.DataJson,
        };

        var outcome = await svc.ProcessAsync(cmd, default);

        outcome.Processed.Should().BeFalse();
        outcome.ErrorReason.Should().Be("case_type_not_found");

        var receipt = await db.CaseIntakes.SingleAsync(x => x.Id == receiptId);
        receipt.Status.Should().Be(IntakeStatus.Failed);
        receipt.ErrorsJson.Should().NotBeNullOrEmpty();
    }

    private sealed class FixedTenantContext(Guid? tenant) : ITenantContext
    {
        public Guid? TenantId { get; private set; } = tenant;
        public IDisposable BeginScope(Guid t)
        {
            var prev = TenantId;
            TenantId = t;
            return new Scope(() => TenantId = prev);
        }
        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}
