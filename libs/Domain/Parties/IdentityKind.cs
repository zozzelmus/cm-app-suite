namespace Conduct.Domain.Parties;

// What KIND of person this Party is. Drives which profile table holds their kind-specific fields.
// "Anonymous" is a flag-driven absence-of-data, not a separate profile table.
public enum IdentityKind
{
    Employee = 1,
    Customer = 2,
    Vendor = 3,
    External = 4,                                                     // any other 3rd-party
    Anonymous = 5,
}
