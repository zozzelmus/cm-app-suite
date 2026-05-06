using Conduct.Domain.Common;

namespace Conduct.Domain.Parties;

public class VendorProfile : TenantedEntity
{
    public Guid PartyId { get; set; }                                 // FK Party (1:1)
    public string VendorId { get; set; } = string.Empty;              // upstream procurement id
    public string? VendorTaxId { get; set; }
    public string? ContractRef { get; set; }
    public string? PrimaryContactEmail { get; set; }
}
