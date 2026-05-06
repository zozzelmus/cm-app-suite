using AwesomeAssertions;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Api.Tests.Seed;

[Collection("postgres")]
public class SeederIdempotencyTests(PostgresFixture pg)
{
    [Fact]
    public async Task SeedAsync_RunOnce_PopulatesBaseLobsAndCaseTypeAndRolesAndDemoUser()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var seeder = new Seeder(db);

        await seeder.SeedAsync();

        // 9 base LOBs (3 root: SUI, CMP, INV (parent of 3 regional), HR-ER, LEG, IA + 3 INV-* children = 9 total)
        (await db.Lobs.CountAsync()).Should().Be(9);
        (await db.CaseTypes.CountAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey)).Should().Be(1);
        (await db.Roles.CountAsync(x => x.IsBuiltIn)).Should().Be(5);
        (await db.Users.CountAsync(x => x.KeycloakSub == SeedConstants.DemoUserKeycloakSub)).Should().Be(1);
        (await db.Parties.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await db.EmployeeProfiles.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateRows()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var seeder = new Seeder(db);

        await seeder.SeedAsync();
        var lobsAfterFirst = await db.Lobs.CountAsync();
        var caseTypesAfterFirst = await db.CaseTypes.CountAsync();
        var rolesAfterFirst = await db.Roles.CountAsync();
        var usersAfterFirst = await db.Users.CountAsync();
        var assignmentsAfterFirst = await db.Assignments.CountAsync();

        await seeder.SeedAsync();

        (await db.Lobs.CountAsync()).Should().Be(lobsAfterFirst);
        (await db.CaseTypes.CountAsync()).Should().Be(caseTypesAfterFirst);
        (await db.Roles.CountAsync()).Should().Be(rolesAfterFirst);
        (await db.Users.CountAsync()).Should().Be(usersAfterFirst);
        (await db.Assignments.CountAsync()).Should().Be(assignmentsAfterFirst);
    }

    [Fact]
    public async Task SeedAsync_LobTree_HasExpectedHierarchy()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var seeder = new Seeder(db);

        await seeder.SeedAsync();

        var inv = await db.Lobs.SingleAsync(x => x.ShortCode == SeedConstants.LobInvestigations);
        var invApac = await db.Lobs.SingleAsync(x => x.ShortCode == SeedConstants.LobInvestigationsApac);
        var invIndia = await db.Lobs.SingleAsync(x => x.ShortCode == SeedConstants.LobInvestigationsIndia);
        var invPhilippines = await db.Lobs.SingleAsync(x => x.ShortCode == SeedConstants.LobInvestigationsPhilippines);

        invApac.ParentLobId.Should().Be(inv.Id);
        invIndia.ParentLobId.Should().Be(inv.Id);
        invPhilippines.ParentLobId.Should().Be(inv.Id);
        inv.ParentLobId.Should().BeNull();
    }

    [Fact]
    public async Task SeedAsync_DemoUserAssignment_GrantsInvestigatorOnInvApac()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var seeder = new Seeder(db);

        await seeder.SeedAsync();

        var demoUser = await db.Users.SingleAsync(x => x.KeycloakSub == SeedConstants.DemoUserKeycloakSub);
        var investigatorRole = await db.Roles.SingleAsync(x => x.Name == SeedConstants.RoleInvestigator);
        var invApacLob = await db.Lobs.SingleAsync(x => x.ShortCode == SeedConstants.LobInvestigationsApac);

        var assignment = await db.Assignments.SingleAsync(a =>
            a.SubjectId == demoUser.Id &&
            a.RoleId == investigatorRole.Id &&
            a.ScopeId == invApacLob.Id);

        assignment.SubjectType.Should().Be(Conduct.Domain.Authorization.AssignmentSubjectType.User);
        assignment.ScopeType.Should().Be(Conduct.Domain.Authorization.AssignmentScopeType.Lob);
    }
}
