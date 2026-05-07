namespace Conduct.Infrastructure.Authorization;

// Closed hierarchy describing the scope of a permission check at request-time.
//
// `Global`        — applies regardless of LOB or CaseType (e.g. `system.admin`)
// `Lob(lobId)`    — caller is acting on something owned by the specified LOB; the
//                   authorization service walks the LOB ancestor chain so an Assignment
//                   on a parent LOB inherits down to descendants.
// `CaseType(id)`  — caller is acting on something governed by the given CaseType.
//
// Sealed/abstract pattern instead of an enum + nullable id field because:
//   1. The id is meaningful only for Lob/CaseType, never for Global.
//   2. We want exhaustive switch in ConductAuthorization without `default: throw`.
public abstract record AuthScope
{
    public sealed record Global : AuthScope
    {
        public static readonly Global Instance = new();
    }

    public sealed record Lob(Guid LobId) : AuthScope;

    public sealed record CaseType(Guid CaseTypeId) : AuthScope;
}
