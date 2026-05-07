namespace Conduct.Infrastructure.Authorization;

// App-DB-driven authorization service. Distinct from ASP.NET's IAuthorizationService —
// that one runs the policy/handler graph; this one answers a single question: does this
// user have this permission for this scope, RIGHT NOW, given the current Assignments +
// GroupMemberships + Roles in the database?
//
// Used both by the imperative endpoint checks (`POST /api/cases` parses lobShortCode then
// calls HasPermissionAsync) and by the ASP.NET ConductPermissionHandler that backs the
// `RequiresPermission` attribute for global-scope endpoints.
public interface IConductAuthorization
{
    // True iff the user has the named permission at the requested scope.
    // Walks effective Assignments (direct + via GroupMembership), filters by ScopeType
    // match, expands LOB ancestor chain.
    Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        AuthScope scope,
        CancellationToken ct = default);
}
