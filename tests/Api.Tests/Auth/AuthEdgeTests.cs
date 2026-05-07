using System.Net.Http.Json;
using AwesomeAssertions;
using Conduct.Infrastructure.Seed;

namespace Conduct.Api.Tests.Auth;

public sealed class AuthEdgeTests : IClassFixture<AuthEdgeFactory>
{
    private readonly AuthEdgeFactory _factory;

    public AuthEdgeTests(AuthEdgeFactory factory) => _factory = factory;

    [Fact]
    public async Task PostCases_anonymous_returns_401_from_authz_layer_not_tenant_middleware()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "INV-APAC",
            title = "anon",
            data = new { summary = "x" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Anonymous requests are stopped by the authorization layer (FallbackPolicy =
        // RequireAuthenticatedUser) BEFORE the tenant middleware runs. If a future regression
        // disables FallbackPolicy, the request would reach TenantContextMiddleware and 401
        // with `tenant_unknown` instead — that body would falsely look like "auth works".
        // Asserting the absence of the tenant marker pins the boundary.
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain("tenant_unknown");
    }

    [Fact]
    public async Task PostCases_authed_without_tenant_claim_returns_401_tenant_unknown()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "test-user-1");
        // intentionally NOT adding TenantHeader

        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "INV-APAC",
            title = "no tenant",
            data = new { summary = "x" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("tenant_unknown");
    }

    [Fact]
    public async Task GetIntakeStatus_authed_with_tenant_claim_passes_auth_to_endpoint()
    {
        // Auth + tenant resolution succeed, so the request reaches the endpoint, which
        // returns 404 because the receipt id doesn't exist in the DB. 404 (not 401) is the
        // correct signal that the auth/tenant boundary let us through.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "test-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());

        var resp = await client.GetAsync($"/api/cases/intake/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIntakeStatus_anonymous_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/cases/intake/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Alive_endpoint_remains_anonymous()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/alive");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EchoEndpoint_anonymous_returns_401()
    {
        // The /api/_meta/echo endpoint exists for BFF→YARP→API sanity. It must NOT be
        // anonymous — otherwise an unauthenticated path-fall-through (or a misconfigured
        // BFF YARP route) could bypass auth. AC4: every endpoint under /api is auth-required
        // by FallbackPolicy.
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/_meta/echo");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EchoEndpoint_authed_with_tenant_returns_200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "test-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeedConstants.DemoTenantId.ToString());
        var resp = await client.GetAsync("/api/_meta/echo");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostCases_authed_with_malformed_tenant_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "test-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "not-a-guid");

        var resp = await client.PostAsJsonAsync("/api/cases", new
        {
            caseTypeKey = "default",
            lobShortCode = "INV-APAC",
            title = "malformed",
            data = new { summary = "x" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("tenant_unknown");
    }
}
