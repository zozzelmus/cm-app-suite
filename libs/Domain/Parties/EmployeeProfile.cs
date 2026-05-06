using Conduct.Domain.Common;

namespace Conduct.Domain.Parties;

// Employee-specific fields. 1:1 w/ Party where IdentityKind=Employee.
public class EmployeeProfile : TenantedEntity
{
    public Guid PartyId { get; set; }                                 // FK Party (1:1)
    public string EmployeeId { get; set; } = string.Empty;            // upstream HR system id
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public Guid? ManagerEmployeePartyId { get; set; }                 // FK Party (manager's Party.Id)
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Office { get; set; }
    public string? SourceSystemRef { get; set; }                      // e.g., "workday:12345" — provenance of this profile
}
