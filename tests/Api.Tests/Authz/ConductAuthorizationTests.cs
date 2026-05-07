using AwesomeAssertions;
using Conduct.Api.Tests.Seed;
using Conduct.Domain.Authorization;
using Conduct.Domain.Lobs;
using Conduct.Domain.Identity;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Authorization;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using static Conduct.Infrastructure.Seed.SeedConstants;

namespace Conduct.Api.Tests.Authz;

// DB-backed unit tests for ConductAuthorization. Each test gets a freshly-migrated DB and
// inserts a hand-crafted graph (Lob tree + User + Role + Assignment) so we can test exact
// scope-matching semantics without depending on the seeded demo data.
[Collection("postgres")]
public class ConductAuthorizationTests(PostgresFixture pg)
{
    private static readonly Guid Tenant = SeedConstants.DemoTenantId;

    [Fact]
    public async Task User_with_LobScope_assignment_has_permission_on_that_lob()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var (userId, _, _, lobId) = await SeedMinimalGraphAsync(db, "INV-APAC", parent: null);

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(userId, Permissions.CaseCreate, new AuthScope.Lob(lobId)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task User_does_not_have_permission_on_a_sibling_lob()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var (userId, _, _, _) = await SeedMinimalGraphAsync(db, "INV-APAC", parent: null);
        var siblingLob = AddLob(db, Tenant, "INV-IN", parentId: null);
        await db.SaveChangesAsync();

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(userId, Permissions.CaseCreate, new AuthScope.Lob(siblingLob.Id)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Assignment_on_parent_lob_inherits_to_child_lob()
    {
        await using var db = await pg.CreateFreshDbAsync();
        // Parent INV with Investigator/case.create; child INV-APAC inheriting.
        var parent = AddLob(db, Tenant, "INV", parentId: null);
        await db.SaveChangesAsync();
        var child = AddLob(db, Tenant, "INV-APAC", parentId: parent.Id);
        await db.SaveChangesAsync();

        var role = AddRoleWithPerm(db, Tenant, "Investigator", Permissions.CaseCreate);
        await db.SaveChangesAsync();

        var user = AddUser(db, Tenant, "kc-user-parent");
        await db.SaveChangesAsync();
        AddAssignment(db, Tenant, user.Id, role.Id, AssignmentScopeType.Lob, parent.Id);
        await db.SaveChangesAsync();

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(user.Id, Permissions.CaseCreate, new AuthScope.Lob(child.Id)))
            .Should().BeTrue("child LOB inherits permissions granted on the parent");
    }

    [Fact]
    public async Task Global_assignment_satisfies_lob_scoped_check()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var lob = AddLob(db, Tenant, "INV-APAC", parentId: null);
        var role = AddRoleWithPerm(db, Tenant, "SystemAdmin", Permissions.CaseCreate);
        var user = AddUser(db, Tenant, "kc-system");
        await db.SaveChangesAsync();
        AddAssignment(db, Tenant, user.Id, role.Id, AssignmentScopeType.Global, scopeId: null);
        await db.SaveChangesAsync();

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(user.Id, Permissions.CaseCreate, new AuthScope.Lob(lob.Id)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task User_with_no_assignments_has_no_permissions()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var lob = AddLob(db, Tenant, "INV-APAC", parentId: null);
        var user = AddUser(db, Tenant, "kc-orphan");
        await db.SaveChangesAsync();

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(user.Id, Permissions.CaseCreate, new AuthScope.Lob(lob.Id)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Group_membership_yields_permission_via_group_assignment()
    {
        await using var db = await pg.CreateFreshDbAsync();
        var lob = AddLob(db, Tenant, "INV-APAC", parentId: null);
        var role = AddRoleWithPerm(db, Tenant, "Investigator", Permissions.CaseCreate);
        var user = AddUser(db, Tenant, "kc-via-group");
        var group = new Group { TenantId = Tenant, Name = "Investigators-APAC" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.GroupMemberships.Add(new GroupMembership { TenantId = Tenant, GroupId = group.Id, UserId = user.Id });
        AddAssignment(db, Tenant, group.Id, role.Id, AssignmentScopeType.Lob, lob.Id, AssignmentSubjectType.Group);
        await db.SaveChangesAsync();

        var auth = new ConductAuthorization(db);
        (await auth.HasPermissionAsync(user.Id, Permissions.CaseCreate, new AuthScope.Lob(lob.Id)))
            .Should().BeTrue();
    }

    // ────────── helpers ──────────

    // Inserts a tenant tagged Lob/Role/User/Assignment chain and returns the relevant ids.
    private static async Task<(Guid userId, Guid roleId, Guid assignmentId, Guid lobId)>
        SeedMinimalGraphAsync(ConductDbContext db, string lobShortCode, Guid? parent)
    {
        var lob = AddLob(db, Tenant, lobShortCode, parent);
        var role = AddRoleWithPerm(db, Tenant, "Investigator", Permissions.CaseCreate);
        var user = AddUser(db, Tenant, $"kc-{Guid.NewGuid():N}");
        await db.SaveChangesAsync();
        var asn = AddAssignment(db, Tenant, user.Id, role.Id, AssignmentScopeType.Lob, lob.Id);
        await db.SaveChangesAsync();
        return (user.Id, role.Id, asn.Id, lob.Id);
    }

    private static Lob AddLob(ConductDbContext db, Guid tenant, string code, Guid? parentId)
    {
        var l = new Lob
        {
            TenantId = tenant,
            Name = code,
            ShortCode = code,
            ParentLobId = parentId,
            ApprovalQuorum = ApprovalQuorum.AnyOneManager,
        };
        db.Lobs.Add(l);
        return l;
    }

    private static Role AddRoleWithPerm(ConductDbContext db, Guid tenant, string name, params string[] perms)
    {
        var r = new Role { TenantId = tenant, Name = name, IsBuiltIn = false, Permissions = perms };
        db.Roles.Add(r);
        return r;
    }

    private static User AddUser(ConductDbContext db, Guid tenant, string sub)
    {
        var u = new User
        {
            TenantId = tenant,
            KeycloakSub = sub,
            Username = sub,
            Email = $"{sub}@conduct.test",
            IsActive = true,
        };
        db.Users.Add(u);
        return u;
    }

    private static Assignment AddAssignment(
        ConductDbContext db,
        Guid tenant,
        Guid subjectId,
        Guid roleId,
        AssignmentScopeType scopeType,
        Guid? scopeId,
        AssignmentSubjectType subjectType = AssignmentSubjectType.User)
    {
        var a = new Assignment
        {
            TenantId = tenant,
            SubjectType = subjectType,
            SubjectId = subjectId,
            RoleId = roleId,
            ScopeType = scopeType,
            ScopeId = scopeId,
        };
        db.Assignments.Add(a);
        return a;
    }
}
