using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Conduct.Bff.Auth;

// Dev-only login bypass for Playwright. Performs a Resource Owner Password Credentials Grant
// against Keycloak using the BFF's confidential client credentials, then signs the resulting
// principal in to the cookie scheme. Tokens are persisted on the auth ticket so the YARP
// access-token transform can forward them to the API exactly as a real OIDC login would.
//
// Defence-in-depth gating:
//   * Registration-time: route is only mapped when IsDevelopment() AND Auth:TestLogin:Enabled
//     are both true at startup. Caps the blast radius for prod-built images.
//   * Request-time: the handler re-checks both gates on every call. Catches dev-built
//     images shipped to prod-like envs and config-reload toggles.
//   * appsettings.json explicitly sets Auth:TestLogin:Enabled=false; appsettings.Development.json
//     flips it true. Belt and braces.
//
// Reads credentials from a JSON body (or empty body for defaults). Querystring would put
// passwords in access logs / browser history.
public static class TestLoginEndpoint
{
    public sealed record TestLoginRequest(string? Username, string? Password);

    public sealed record TestLoginResponse(string Sub, string? TenantId, string? PreferredUsername);

    public static IEndpointRouteBuilder MapTestLoginIfEnabled(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;
        if (!app.Configuration.GetValue("Auth:TestLogin:Enabled", false)) return app;

        app.MapPost("/bff/_test/login-as", async (
            [FromBody] TestLoginRequest? body,
            [FromServices] IHttpClientFactory httpFactory,
            [FromServices] IConfiguration config,
            [FromServices] IHostEnvironment env,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // Runtime re-check (see comment block above).
            if (!env.IsDevelopment() || !config.GetValue("Auth:TestLogin:Enabled", false))
            {
                return Results.NotFound();
            }

            var resolvedUsername = string.IsNullOrEmpty(body?.Username)
                ? config["Auth:TestLogin:DefaultUsername"] ?? "demo"
                : body.Username;
            var resolvedPassword = string.IsNullOrEmpty(body?.Password)
                ? config["Auth:TestLogin:DefaultPassword"] ?? "demo"
                : body.Password;

            var authority = config["Auth:Authority"] ?? "http://localhost:8088/realms/conduct";
            var clientId = config["Auth:ClientId"] ?? "conduct-bff";
            var clientSecret = config["Auth:ClientSecret"] ?? "dev-secret";

            var http = httpFactory.CreateClient("keycloak-test-login");

            var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["username"] = resolvedUsername,
                ["password"] = resolvedPassword,
                ["scope"] = "openid profile email conduct:use",
            });

            using var tokenResp = await http.PostAsync($"{authority}/protocol/openid-connect/token", tokenForm, ct);
            if (!tokenResp.IsSuccessStatusCode)
            {
                var detail = await tokenResp.Content.ReadAsStringAsync(ct);
                return Results.Json(new
                {
                    error = "test_login_failed",
                    statusCode = (int)tokenResp.StatusCode,
                    detail,
                }, statusCode: 502);
            }

            using var tokenJson = await JsonDocument.ParseAsync(
                await tokenResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = tokenJson.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var idToken = root.TryGetProperty("id_token", out var idTok) ? idTok.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900;

            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(accessToken);

            var identity = new ClaimsIdentity(jwt.Claims, CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: "preferred_username", roleType: "roles");
            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            };
            var tokens = new List<AuthenticationToken>
            {
                new() { Name = "access_token", Value = accessToken },
                new() { Name = "token_type", Value = "Bearer" },
                new() { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o") },
            };
            if (!string.IsNullOrEmpty(idToken))      tokens.Add(new AuthenticationToken { Name = "id_token", Value = idToken });
            if (!string.IsNullOrEmpty(refreshToken)) tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
            props.StoreTokens(tokens);

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

            var sub = principal.FindFirst("sub")?.Value
                   ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? string.Empty;
            var tenantId = principal.FindFirst("tenant_id")?.Value;
            var preferredUsername = principal.FindFirst("preferred_username")?.Value;

            return Results.Ok(new TestLoginResponse(sub, tenantId, preferredUsername));
        }).AllowAnonymous().WithTags("DevTestLogin");

        return app;
    }
}
