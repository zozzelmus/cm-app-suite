using System.Net.Http.Json;
using AwesomeAssertions;
using Conduct.Api.Tests.Auth;
using Conduct.Infrastructure;
using Conduct.Infrastructure.Multitenancy;
using Conduct.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Api.Tests.Authz;

// Endpoint-level integration tests for F10's per-endpoint permission gate. Reuses the F9
// AuthEdgeFactory (which now seeds the demo data) so the demo user has the seeded
// Investigator/INV-APAC assignment.
public sealed class PermissionEdgeTests : IClassFixture<AuthEdgeFactory>
{
    private readonly AuthEdgeFactory _factory;

    public PermissionEdgeTests(AuthEdgeFactory factory) => _factory = factory;

    [Fact]
    public async Task PostCases_demo_user_with_inv_apac_assignment_returns_202()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, SeedConstants.DemoUserKeycloakSub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "INV-APAC",
            title = "Permitted submission",
            data = new { summary = "Permitted summary", severity = "Low" },
        });

        resp.IsSuccessStatusCode.Should().BeTrue($"demo user has Investigator on INV-APAC; body: {await resp.Content.ReadAsStringAsync()}");
        ((int)resp.StatusCode).Should().Be(202);
    }

    [Fact]
    public async Task PostCases_user_without_assignment_returns_403_permission_denied()
    {
        // A fresh sub gets JIT-mirrored as a User row but has no Assignment.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, $"kc-no-perm-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "INV-APAC",
            title = "Should-be-blocked",
            data = new { summary = "Blocked summary", severity = "Low" },
        });

        ((int)resp.StatusCode).Should().Be(403);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("permission_denied");
    }

    [Fact]
    public async Task PostCases_demo_user_targeting_unrelated_lob_returns_403()
    {
        // Demo user has Investigator on INV-APAC but NOT on LEG. Same 403 path as unknown LOB
        // — endpoint deliberately does not distinguish (don't leak LOB existence).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, SeedConstants.DemoUserKeycloakSub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "LEG",
            title = "wrong-lob",
            data = new { summary = "x", severity = "Low" },
        });

        ((int)resp.StatusCode).Should().Be(403);
    }

    [Fact]
    public async Task First_request_jit_creates_user_row()
    {
        var sub = $"kc-jit-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        // Hit any auth-required endpoint to trigger the mirror.
        var first = await client.GetAsync("/api/_meta/echo");
        first.IsSuccessStatusCode.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginScope(SeedConstants.DemoTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ConductDbContext>();
        var row = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.KeycloakSub == sub);
        row.Should().NotBeNull("JIT mirror should have inserted a User row on first authenticated request");
        row!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Inactive_user_returns_403_user_deactivated()
    {
        var sub = $"kc-inactive-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        // First request JIT-creates the user.
        await client.GetAsync("/api/_meta/echo");

        // Deactivate the user directly.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tenant.BeginScope(SeedConstants.DemoTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ConductDbContext>();
            // ExecuteUpdate would be cleaner but EnsureCreated path doesn't apply migrations
            // to the test fixture; raw SQL keeps the test independent of migration runs.
            var user = await db.Users.SingleAsync(u => u.KeycloakSub == sub);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/api/_meta/echo");
        ((int)resp.StatusCode).Should().Be(403);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("user_deactivated");
    }
}
