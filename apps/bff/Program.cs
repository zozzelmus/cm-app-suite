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
