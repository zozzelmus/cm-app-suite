using Conduct.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure.Authorization;

// Permission-resolution implementation backed by ConductDbContext.
//
// Two round trips per check:
//   * Subject expansion: pull the user's GroupMembership rows so we can union (user-direct +
//     via-group) Assignments inside a single subject filter.
//   * Lob scope only: an extra recursive-CTE round trip to enumerate the target LOB's
//     ancestor chain (typically 1–4 rows).
// Then the assignment + role join becomes a single query.
//
// RLS scopes the underlying tables to the current tenant via the connection-level GUC, so
// no explicit TenantId predicate is needed.
public sealed class ConductAuthorization(ConductDbContext db) : IConductAuthorization
{
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        AuthScope scope,
        CancellationToken ct = default)
    {
        // Subject filter: assignments granted directly to the user OR to any group the user is in.
        var groupIds = await db.GroupMemberships
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => g.GroupId)
            .ToListAsync(ct);

        var subjectAssignments = db.Assignments.AsNoTracking().Where(a =>
            (a.SubjectType == AssignmentSubjectType.User && a.SubjectId == userId) ||
            (a.SubjectType == AssignmentSubjectType.Group && groupIds.Contains(a.SubjectId)));

        // Scope filter — exhaustive switch over the closed AuthScope hierarchy.
        IQueryable<Assignment> scoped;
        switch (scope)
        {
            case AuthScope.Global:
                scoped = subjectAssignments.Where(a => a.ScopeType == AssignmentScopeType.Global);
                break;

            case AuthScope.Lob lob:
            {
                // Materialise ancestor ids — EF can't compose a SqlQueryRaw into a LINQ
                // Contains(), so we round-trip them and pass as a List<Guid>.
                var ancestorIds = await LobAncestorIdsAsync(lob.LobId, ct);
                scoped = subjectAssignments.Where(a =>
                    a.ScopeType == AssignmentScopeType.Global ||
                    (a.ScopeType == AssignmentScopeType.Lob &&
                     a.ScopeId != null &&
                     ancestorIds.Contains(a.ScopeId.Value)));
                break;
            }

            case AuthScope.CaseType ctScope:
                scoped = subjectAssignments.Where(a =>
                    a.ScopeType == AssignmentScopeType.Global ||
                    (a.ScopeType == AssignmentScopeType.CaseType && a.ScopeId == ctScope.CaseTypeId));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }

        // Resolve to roles, then ask Postgres to test array containment.
        var roleIds = await scoped.Select(a => a.RoleId).Distinct().ToListAsync(ct);
        if (roleIds.Count == 0) return false;

        return await db.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id) && r.Permissions.Contains(permission))
            .AnyAsync(ct);
    }

    // Returns the target LOB id plus all of its ancestors via a Postgres recursive CTE.
    private async Task<List<Guid>> LobAncestorIdsAsync(Guid startLobId, CancellationToken ct)
    {
        return await db.Database
            .SqlQueryRaw<Guid>(
                """
                WITH RECURSIVE ancestors(id, parent) AS (
                    SELECT "Id", "ParentLobId" FROM "Lobs" WHERE "Id" = {0}
                    UNION ALL
                    SELECT l."Id", l."ParentLobId"
                      FROM "Lobs" l JOIN ancestors a ON l."Id" = a.parent
                )
                SELECT id FROM ancestors
                """,
                startLobId)
            .ToListAsync(ct);
    }
}
