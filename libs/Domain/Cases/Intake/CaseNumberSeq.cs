using Conduct.Domain.Common;

namespace Conduct.Domain.Cases.Intake;

// Per-(Tenant, Year, LOB-short-code) counter for CaseNumber allocation. Each row holds the
// NEXT value to allocate; allocation = atomic UPSERT-then-increment via Postgres
// `INSERT ... ON CONFLICT DO UPDATE ... RETURNING (NextValue - 1)`.
//
// We use a table (not a Postgres sequence) because sequence names would otherwise need to
// encode tenant id in the identifier, which blows past Postgres' 63-char identifier limit.
// Table approach gives clean per-tenant scoping + survives RLS naturally.
public class CaseNumberSeq : TenantedEntity
{
    public int Year { get; set; }
    public string LobShortCode { get; set; } = string.Empty;
    public long NextValue { get; set; } = 1;
}
