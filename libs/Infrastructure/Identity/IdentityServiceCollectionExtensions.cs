using Microsoft.AspNetCore.Builder;

namespace Conduct.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Pipeline registration. Place BETWEEN UseTenantContext and UseAuthorization so the JIT
    // mirror has an ambient tenant set before its DbContext access runs, and so the
    // resulting `app_user_id` claim is on the principal by the time per-endpoint
    // permission policies are evaluated.
    public static IApplicationBuilder UseUserMirror(this IApplicationBuilder app)
        => app.UseMiddleware<UserMirrorMiddleware>();
}
