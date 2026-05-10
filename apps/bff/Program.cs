using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var authority = builder.Configuration["Auth:Authority"] ?? "http://localhost:8088/realms/conduct";
var clientId = builder.Configuration["Auth:ClientId"] ?? "conduct-bff";
var clientSecret = builder.Configuration["Auth:ClientSecret"] ?? "dev-secret";

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "__Host-conduct";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = authority;
        o.ClientId = clientId;
        o.ClientSecret = clientSecret;
        o.ResponseType = "code";
        o.UsePkce = true;
        o.SaveTokens = true;
        o.GetClaimsFromUserInfoEndpoint = true;
        o.RequireHttpsMetadata = false; // dev only — Keycloak local is http
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF");
builder.Services.AddHttpClient();

builder.Services.AddReverseProxy()
    .LoadFromMemory(
        routes:
        [
            new RouteConfig
            {
                RouteId = "api",
                ClusterId = "api",
                Match = new RouteMatch { Path = "/api/{**catch-all}" }
            }
        ],
        clusters:
        [
            new ClusterConfig
            {
                ClusterId = "api",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["primary"] = new() { Address = "http://api/" }
                }
            }
        ])
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/bff/user", (HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new
        {
            name = ctx.User.Identity.Name,
            claims = ctx.User.Claims.Select(c => new { c.Type, c.Value })
        })
        : Results.Unauthorized());

app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapPost("/bff/logout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

app.MapReverseProxy();

// ────────── Dev-only test user switcher endpoints ──────────
// Lets the SPA list known seeded test users and "log in as" any of them via Keycloak's
// Resource Owner Password Credentials grant (directAccessGrantsEnabled on the client).
//
// Defense in depth (review feedback): IsDevelopment alone is insufficient — an
// `ASPNETCORE_ENVIRONMENT=Development` misconfiguration on a real env would expose
// password-grant login on a known username list. We additionally gate on:
//   1. Explicit config opt-in `Dev:EnableLoginAs=true` (defaults false outside Dev)
//   2. Per-request loopback check — refuse anything that didn't originate from localhost
// Antiforgery stays off ONLY for the login-as POST because the SPA's CSRF token comes
// from a cookie that doesn't yet exist on first call; the loopback guard is the gate.
var devLoginEnabled = builder.Configuration.GetValue("Dev:EnableLoginAs", app.Environment.IsDevelopment());
if (devLoginEnabled)
{
    var dev = app.MapGroup("/_dev").DisableAntiforgery();
    dev.AddEndpointFilter(async (ctx, next) =>
    {
        var remote = ctx.HttpContext.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            return Results.NotFound();
        return await next(ctx);
    });

    // Keep this in sync with infra/keycloak/realm/conduct-realm.json and
    // libs/Infrastructure/Seed/SeedConstants.cs. Drift would surface as missing entries
    // in the picker; harmless but confusing.
    var testUsers = new (string Username, string Label, string Role, string Scope)[]
    {
        ("mgr-sui",       "Mia Manager-SUI",      "LOB Manager",        "Speak-Up Intake"),
        ("inv-sui",       "Ian Investigator-SUI", "Investigator",       "Speak-Up Intake"),
        ("mgr-cmp",       "Mara Manager-CMP",     "LOB Manager",        "Compliance"),
        ("inv-cmp",       "Igor Investigator-CMP","Investigator",       "Compliance"),
        ("mgr-inv",       "Mei Manager-INV",      "LOB Manager",        "Investigations"),
        ("inv-inv",       "Ivo Investigator-INV", "Investigator",       "Investigations"),
        ("mgr-inv-apac",  "Mina Manager-APAC",    "LOB Manager",        "Investigations APAC"),
        ("inv-inv-apac",  "Ines Investigator-APAC","Investigator",      "Investigations APAC"),
        ("mgr-inv-in",    "Maya Manager-IN",      "LOB Manager",        "Investigations India"),
        ("inv-inv-in",    "Ishan Investigator-IN","Investigator",       "Investigations India"),
        ("mgr-inv-ph",    "Marcos Manager-PH",    "LOB Manager",        "Investigations Philippines"),
        ("inv-inv-ph",    "Iris Investigator-PH", "Investigator",       "Investigations Philippines"),
        ("mgr-hr-er",     "Marta Manager-HR-ER",  "LOB Manager",        "Employee Relations"),
        ("inv-hr-er",     "Ilia Investigator-HR-ER","Investigator",     "Employee Relations"),
        ("mgr-leg",       "Milo Manager-LEG",     "LOB Manager",        "Legal"),
        ("inv-leg",       "Iona Investigator-LEG","Investigator",       "Legal"),
        ("mgr-ia",        "Mila Manager-IA",      "LOB Manager",        "Internal Audit"),
        ("inv-ia",        "Ilan Investigator-IA", "Investigator",       "Internal Audit"),
        ("sysadmin",      "Sam System-Admin",     "System Admin",       "Global"),
    };

    dev.MapGet("/users", () => Results.Ok(
        testUsers.Select(u => new { username = u.Username, label = u.Label, role = u.Role, scope = u.Scope })));

    dev.MapPost("/login-as", async (LoginAsRequest req, HttpContext ctx, IHttpClientFactory httpFactory) =>
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return Results.BadRequest(new { error = "username required" });

        using var http = httpFactory.CreateClient();

        // ROPC: password grant. The KC client must have directAccessGrantsEnabled.
        var tokenResp = await http.PostAsync($"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["username"] = req.Username,
                ["password"] = req.Username, // POC: pwd == username
                ["scope"] = "openid profile email",
            }));
        if (!tokenResp.IsSuccessStatusCode)
        {
            var body = await tokenResp.Content.ReadAsStringAsync();
            return Results.Problem(detail: body, statusCode: (int)tokenResp.StatusCode, title: "KC token request failed");
        }
        using var tokens = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());

        // The id_token is the OIDC-standard place for user identity claims. Decoding it
        // directly avoids needing the realm's profile/email scopes to have explicit mappers
        // (our dev seed creates them empty, so /userinfo would only return `sub`).
        //
        // SAFETY: signature is NOT verified here. Safe ONLY because we just minted this
        // token ourselves against the local KC instance via password grant in this same
        // request — there is no untrusted intermediary. DO NOT lift this decoder into any
        // path that accepts an externally-supplied token; validate via JwtSecurityTokenHandler
        // + KC's JWKS before trusting claims in that case.
        var idToken = tokens.RootElement.GetProperty("id_token").GetString()!;
        var payload = idToken.Split('.')[1];
        // Base64Url -> Base64
        var b64 = payload.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + ((4 - b64.Length % 4) % 4), '=');
        using var idTokenJson = JsonDocument.Parse(Convert.FromBase64String(b64));

        var claims = new List<Claim>();
        foreach (var prop in idTokenJson.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in prop.Value.EnumerateArray())
                    claims.Add(new Claim(prop.Name, el.ToString()));
            }
            else
            {
                claims.Add(new Claim(prop.Name, prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()!
                    : prop.Value.GetRawText()));
            }
        }

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: "preferred_username",
            roleType: "roles");
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.NoContent();
    });
}

if (app.Environment.IsDevelopment())
{
    // Read the Aspire-injected Vite dev URL. WithReference(web) at the AppHost binds
    // services__web__http__0=<viteUrl> into our env, surfaced by Configuration as services:web:http:0.
    // MapForwarder doesn't auto-resolve service-discovery names the way cluster destinations do,
    // so we resolve once at startup.
    var webUrl = app.Configuration["services:web:http:0"]
              ?? builder.Configuration["services:web:http:0"]
              ?? "http://localhost:5173";
    app.MapForwarder("/{**catch-all}", webUrl);
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

internal sealed record LoginAsRequest(string Username);
