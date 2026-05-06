using Conduct.Domain.Common;

namespace Conduct.Domain.Identity;

// App-side mirror of the Keycloak user identity. Created via JIT on first login.
// PartyId optionally links to the Party representing this user (typically EmployeeProfile-backed).
// System / API service accounts have PartyId = null.
public class User : TenantedEntity
{
    public string KeycloakSub { get; set; } = string.Empty;           // OIDC subject claim — unique per tenant
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Guid? PartyId { get; set; }                                // links to the Party representing this user (optional)
    public string? ExternalUserId { get; set; }                       // upstream HR system id (Workday, etc.)
    public bool IsActive { get; set; } = true;                        // false = deprovisioned (effective permissions revoked)
    public DateTimeOffset? LastLoginAt { get; set; }
}
