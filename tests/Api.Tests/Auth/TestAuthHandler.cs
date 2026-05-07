using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conduct.Api.Tests.Auth;

// Substitutes for the JwtBearer scheme inside WebApplicationFactory tests. Reads two custom
// headers and builds a principal:
//   X-Test-Sub:    arbitrary sub claim (default: "test-user-1")
//   X-Test-Tenant: tenant_id claim (omit to simulate a token with no tenant_id)
//
// If neither header is present, the handler returns NoResult so the request is treated as
// anonymous — that lets the same factory cover both authed and unauthed cases without two
// scheme registrations.
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";
    public const string TenantHeader = "X-Test-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sub = Request.Headers[SubHeader].ToString();
        var tenant = Request.Headers[TenantHeader].ToString();

        if (string.IsNullOrEmpty(sub) && string.IsNullOrEmpty(tenant))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", string.IsNullOrEmpty(sub) ? "test-user-1" : sub) };
        if (!string.IsNullOrEmpty(tenant))
        {
            claims.Add(new Claim("tenant_id", tenant));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", "roles");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
