using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Conduct.Bff.Auth;

// YARP request transform: forward the cookie session's access token to the API as
// `Authorization: Bearer <token>`. Applied to any route that has an AuthorizationPolicy set
// (i.e. the BFF→API forwarding route). Routes without an authz policy are unauthenticated
// passthroughs and don't get a token attached. The dev SPA `MapForwarder("/{**catch-all}")`
// goes through a separate forwarder pipeline (not MapReverseProxy), so this transform does
// not fire on it — Vite assets stay anonymous.
//
// Removes any inbound `Authorization` header before re-attaching from the auth ticket — this
// blocks header-smuggling from a downgraded (un-authed) session. Without the explicit
// remove, an unauthenticated request that nevertheless reached the transform with an
// `Authorization` header would be relayed to the API verbatim.
//
// TODO (F10/F11): refresh expired access tokens.
//   Cookie has 8h sliding expiry; access token has 900s lifespan. Currently a stale token is
//   forwarded as-is and the API 401s — the user appears logged in but every request fails.
//   Wire `Duende.AccessTokenManagement.OpenIdConnect` (or `UseTokenLifetime=true` on the
//   OpenIdConnectOptions) to refresh transparently. Tracked in docs/backlog.md.
public sealed class AccessTokenTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        if (string.IsNullOrEmpty(context.Route.AuthorizationPolicy))
        {
            return;
        }

        context.AddRequestTransform(async ctx =>
        {
            ctx.ProxyRequest.Headers.Authorization = null;

            var token = await ctx.HttpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(token))
            {
                ctx.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        });
    }
}
