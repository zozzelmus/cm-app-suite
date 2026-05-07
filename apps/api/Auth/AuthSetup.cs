using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Conduct.Api.Auth;

// API edge: JWT bearer trusting Keycloak as the issuer.
//
// SECURITY — known gap, deferred to F10/F11:
//   * Audience validation is OFF. Any access token signed by the `conduct` realm — including
//     tokens minted for an unrelated client (confused-deputy) or service-account tokens
//     issued for a different audience — will be accepted here. Until per-API audience mapper
//     + carve-out lands, treat issuer-trust as the only gate. Backlog: docs/backlog.md
//     "Audience validation + per-API client carve-out".
//   * Tenant `tenant_id` claim is taken at face value (see TenantContextMiddleware). The
//     paired check that the authenticated user actually belongs to that tenant lands in F10
//     when the User mirror is JIT-provisioned + cross-checked against the claim.
//
// FallbackPolicy = RequireAuthenticatedUser ensures every endpoint is auth-required by
// default. ServiceDefaults health endpoints opt into anonymous via [AllowAnonymous] upstream.
public static class AuthSetup
{
    public const string TenantClaim = "tenant_id";

    public static IServiceCollection AddConductAuth(this IServiceCollection services, IConfiguration config)
    {
        var authority = config["Auth:Authority"] ?? "http://localhost:8088/realms/conduct";
        var requireHttpsMetadata = config.GetValue("Auth:RequireHttpsMetadata", false);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.MapInboundClaims = false; // preserve raw `sub`, `tenant_id` etc. without Microsoft remapping
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = false, // SECURITY: see comment above; tightened in F10/F11
                    ValidateLifetime = true,
                    NameClaimType = "preferred_username",
                    // RoleClaimType = "realm_access.roles" intentionally NOT set — Keycloak emits
                    // realm_access as a nested object and the .NET handler doesn't path-traverse,
                    // so this would resolve to nothing. F10 wires a JwtBearerEvents claims-
                    // transformer that flattens realm_access.roles → individual `role` claims.
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
