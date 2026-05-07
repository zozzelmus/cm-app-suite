# cm-app-suite

Case management for a finance org. Multi-LOB workflow with inter-LOB transfers.

## Quickstart
```
dotnet run --project apps/AppHost
```
Aspire dashboard prints URLs for web, BFF, API, Postgres, Keycloak.

## Layout
- `apps/web` — Vite + React SPA
- `apps/bff` — .NET BFF (YARP + cookie auth)
- `apps/api` — .NET domain API (EF Core + SignalR)
- `apps/AppHost` — Aspire orchestrator
- `libs/` — shared .NET libs
- `tests/` — `.NET` xUnit + Playwright E2E

## Stack
.NET 10 · React 19 · PostgreSQL (pgvector) · Keycloak · Aspire · Azure Container Apps
