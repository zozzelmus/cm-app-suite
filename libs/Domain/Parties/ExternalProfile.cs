using Conduct.Domain.Common;

namespace Conduct.Domain.Parties;

// Catch-all 3rd-party (regulators, external counsel, journalists, etc.).
public class ExternalProfile : TenantedEntity
{
    public Guid PartyId { get; set; }                                 // FK Party (1:1)
    public string? OrgName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}
