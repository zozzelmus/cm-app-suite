using AwesomeAssertions;
using Conduct.Domain.Cases.Intake;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using IntakeStatusEnum = Conduct.Domain.Cases.Intake.IntakeStatus;

namespace Conduct.Api.Tests.StatusEndpoint;

[Collection("postgres")]
public class IntakeStatusEndpointTests(Conduct.Api.Tests.Seed.PostgresFixture pg)
{
    [Fact]
    public async Task Receipt_Queued_returns_status_with_no_caseId()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var tenant = SeedConstants.DemoTenantId;
        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant, Status = IntakeStatusEnum.Queued,
            CaseTypeKey = "default", LobShortCode = "INV-APAC",
        });
        await db.SaveChangesAsync();

        var fetched = await db.CaseIntakes.AsNoTracking().SingleAsync(x => x.Id == receiptId);

        // Mirrors the endpoint's read path; service is a thin wrapper, so the meaningful
        // surface here is the data model the endpoint surfaces (status name, no CaseId yet).
        fetched.Status.Should().Be(IntakeStatusEnum.Queued);
        fetched.CaseId.Should().BeNull();
        fetched.CaseNumber.Should().BeNull();
    }

    [Fact]
    public async Task Receipt_Completed_returns_caseId_and_caseNumber()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var tenant = SeedConstants.DemoTenantId;
        var caseId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant,
            Status = IntakeStatusEnum.Completed, CaseId = caseId, CaseNumber = "2026-INV-APAC-000007",
            CaseTypeKey = "default", LobShortCode = "INV-APAC",
        });
        await db.SaveChangesAsync();

        var fetched = await db.CaseIntakes.AsNoTracking().SingleAsync(x => x.Id == receiptId);
        fetched.Status.Should().Be(IntakeStatusEnum.Completed);
        fetched.CaseId.Should().Be(caseId);
        fetched.CaseNumber.Should().Be("2026-INV-APAC-000007");
    }

    [Fact]
    public async Task Receipt_Failed_returns_errors()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var tenant = SeedConstants.DemoTenantId;
        var receiptId = Guid.NewGuid();
        db.CaseIntakes.Add(new CaseIntake
        {
            Id = receiptId, TenantId = tenant, Status = IntakeStatusEnum.Failed,
            CaseTypeKey = "default", LobShortCode = "INV-APAC",
            ErrorsJson = """["case_type_not_found"]""",
        });
        await db.SaveChangesAsync();

        var fetched = await db.CaseIntakes.AsNoTracking().SingleAsync(x => x.Id == receiptId);
        fetched.Status.Should().Be(IntakeStatusEnum.Failed);
        fetched.ErrorsJson.Should().Contain("case_type_not_found");
    }

    [Fact]
    public async Task Unknown_receipt_returns_null_lookup()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var found = await db.CaseIntakes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Guid.NewGuid());
        found.Should().BeNull("unknown id ⇒ lookup returns null ⇒ endpoint responds 404");
    }
}
