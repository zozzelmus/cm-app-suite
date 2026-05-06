using AwesomeAssertions;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Cases.Intake;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Outbox;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace Conduct.Api.Tests.Intake;

[Collection("postgres")]
public class IntakeServiceTests(Conduct.Api.Tests.Seed.PostgresFixture pg)
{
    private static IntakeRequest BuildValidRequest() => new()
    {
        CaseTypeKey = SeedConstants.DefaultCaseTypeKey,
        LobShortCode = SeedConstants.LobInvestigationsApac,
        Title = "Suspected PAD violation",
        Data = JsonNode.Parse(/* lang=json */ """
        { "summary": "Trader X dealt outside firm windows", "severity": "High" }
        """)!,
    };

    private static (IntakeService svc, ConductDbContext db, TestTenantContext tc) BuildSubject(ConductDbContext db, Guid tenantId)
    {
        var tc = new TestTenantContext(tenantId);
        return (new IntakeService(db, tc), db, tc);
    }

    [Fact]
    public async Task SubmitAsync_validRequest_writesReceiptAndOutboxRow_returnsAcceptedOutcome()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var (svc, _, _) = BuildSubject(db, SeedConstants.DemoTenantId);
        var outcome = await svc.SubmitAsync(BuildValidRequest(), CancellationToken.None);

        outcome.IsAccepted.Should().BeTrue();
        outcome.ReceiptId.Should().NotBeNull();
        outcome.Error.Should().BeNull();

        var receipt = await db.CaseIntakes.SingleAsync();
        receipt.Id.Should().Be(outcome.ReceiptId!.Value);
        receipt.Status.Should().Be(IntakeStatus.Queued);
        receipt.TenantId.Should().Be(SeedConstants.DemoTenantId);
        receipt.CaseTypeKey.Should().Be(SeedConstants.DefaultCaseTypeKey);
        receipt.LobShortCode.Should().Be(SeedConstants.LobInvestigationsApac);

        var outbox = await db.Outbox.SingleAsync();
        outbox.Topic.Should().Be(IntakeService.CreateCaseTopicV1);
        outbox.Key.Should().Be(receipt.Id.ToString());
        // Payload embeds DataJson as an escaped string (""summary""); just verify the field
        // name appears in some form by looking for the plain token.
        outbox.PayloadJson.Should().Contain("summary");
        outbox.PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_unknownCaseType_returnsCaseTypeNotFound_writesNothing()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var (svc, _, _) = BuildSubject(db, SeedConstants.DemoTenantId);
        var req = new IntakeRequest
        {
            CaseTypeKey = "no-such-type",
            LobShortCode = SeedConstants.LobInvestigationsApac,
            Title = "x",
            Data = JsonNode.Parse("{\"summary\":\"x\"}")!,
        };

        var outcome = await svc.SubmitAsync(req, CancellationToken.None);

        outcome.IsAccepted.Should().BeFalse();
        outcome.Error!.Kind.Should().Be(IntakeErrorKind.CaseTypeNotFound);
        (await db.CaseIntakes.CountAsync()).Should().Be(0);
        (await db.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SubmitAsync_unknownLob_returnsLobNotFound_writesNothing()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var (svc, _, _) = BuildSubject(db, SeedConstants.DemoTenantId);
        var req = new IntakeRequest
        {
            CaseTypeKey = SeedConstants.DefaultCaseTypeKey,
            LobShortCode = "NO-SUCH-LOB",
            Title = "x",
            Data = JsonNode.Parse("{\"summary\":\"x\"}")!,
        };

        var outcome = await svc.SubmitAsync(req, CancellationToken.None);

        outcome.IsAccepted.Should().BeFalse();
        outcome.Error!.Kind.Should().Be(IntakeErrorKind.LobNotFound);
        (await db.CaseIntakes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SubmitAsync_missingRequiredField_returnsValidationFailed_withFieldErrors()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var (svc, _, _) = BuildSubject(db, SeedConstants.DemoTenantId);
        var req = new IntakeRequest
        {
            CaseTypeKey = SeedConstants.DefaultCaseTypeKey,
            LobShortCode = SeedConstants.LobInvestigationsApac,
            Title = "x",
            Data = JsonNode.Parse(/* lang=json */ """{"severity":"Low"}""")!, // missing required "summary"
        };

        var outcome = await svc.SubmitAsync(req, CancellationToken.None);

        outcome.IsAccepted.Should().BeFalse();
        outcome.Error!.Kind.Should().Be(IntakeErrorKind.ValidationFailed);
        outcome.Error.FieldErrors.Should().NotBeNull();
        outcome.Error.FieldErrors!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SubmitAsync_extraProperty_returnsValidationFailed()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var (svc, _, _) = BuildSubject(db, SeedConstants.DemoTenantId);
        var req = new IntakeRequest
        {
            CaseTypeKey = SeedConstants.DefaultCaseTypeKey,
            LobShortCode = SeedConstants.LobInvestigationsApac,
            Title = "x",
            Data = JsonNode.Parse(/* lang=json */ """{"summary":"ok","unknownProp":"nope"}""")!,
        };

        var outcome = await svc.SubmitAsync(req, CancellationToken.None);

        outcome.IsAccepted.Should().BeFalse();
        outcome.Error!.Kind.Should().Be(IntakeErrorKind.ValidationFailed);
    }

    [Fact]
    public async Task SubmitAsync_noTenantContext_returnsTenantUnknown()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var svc = new IntakeService(db, new TestTenantContext(null));
        var outcome = await svc.SubmitAsync(BuildValidRequest(), CancellationToken.None);

        outcome.IsAccepted.Should().BeFalse();
        outcome.Error!.Kind.Should().Be(IntakeErrorKind.TenantUnknown);
    }

    private sealed class TestTenantContext(Guid? tenant) : ITenantContext
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
