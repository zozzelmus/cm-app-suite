namespace Conduct.Domain.Cases;

// HOW a Party relates to a Case. Configurable later (admin can add new roles like "LegalCounsel").
// MVP: enum constants seeded as data; `RoleOnCase` field on CaseParty stored as string for forward-compat.
public static class RoleOnCase
{
    public const string Subject = "Subject";                          // alleged actor — anti-collusion rule keyed off this
    public const string Reporter = "Reporter";                        // person who reported the case
    public const string Witness = "Witness";
    public const string Other = "Other";
}
