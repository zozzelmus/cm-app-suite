using System.Security.Claims;
using Conduct.Infrastructure.Authorization;
using Conduct.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Conduct.Api.Auth.Authorization;

// ASP.NET Core authorization integration for the app-DB permission graph.
//
// Two surfaces:
//   * Imperative — endpoint resolves a body-derived scope (e.g. lobShortCode -> lobId) and
//     calls IConductAuthorization.HasPermissionAsync(...) directly. F10 uses this for
//     POST /api/cases.
//   * Declarative — endpoints with no body-derived scope can opt in via
//     `[RequiresPermission(Permissions.X)]` (Global scope). The lazy policy provider
//     synthesises a policy named "Permission:<key>" the first time an endpoint asks for it,
//     so we don't have to register every permission key up front.
//
// F10 ships the infrastructure but doesn't apply [RequiresPermission] to any endpoint —
// added so F11+ admin endpoints can declare permissions inline without re-implementing the
// handler each time.

// ──────────── Attribute ────────────

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizeData
{
    public string Permission { get; } = permission;

    // The lazy provider parses this back out via the "Permission:" prefix.
    public new string? Policy
    {
        get => $"{ConductPermissionPolicyProvider.PolicyPrefix}{Permission}";
        set { /* ASP.NET sets Policy via this setter when the attribute is rehydrated */ }
    }
}

// ──────────── Requirement ────────────

public sealed class ConductPermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

// ──────────── Handler ────────────

public sealed class ConductPermissionHandler(IConductAuthorization auth)
    : AuthorizationHandler<ConductPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        ConductPermissionRequirement requirement)
    {
        var userIdClaim = ctx.User.FindFirst(UserMirrorMiddleware.AppUserIdClaim)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            // No JIT mirror has run, or the claim was stripped. Fail closed.
            return;
        }

        var allowed = await auth.HasPermissionAsync(userId, requirement.Permission, AuthScope.Global.Instance);
        if (allowed) ctx.Succeed(requirement);
    }
}

// ──────────── Lazy policy provider ────────────

public sealed class ConductPermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "Permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new ConductPermissionRequirement(permission))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}
