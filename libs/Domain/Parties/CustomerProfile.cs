using Conduct.Domain.Common;

namespace Conduct.Domain.Parties;

public class CustomerProfile : TenantedEntity
{
    public Guid PartyId { get; set; }                                 // FK Party (1:1)
    public string CustomerId { get; set; } = string.Empty;            // upstream CRM id
    public Guid? AccountManagerEmployeePartyId { get; set; }          // FK Party.Id of the AM (Employee-kind Party)
    public string? Segment { get; set; }                              // e.g., "Retail" | "Institutional" | "Wealth"
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
