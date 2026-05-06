# Deploy

## Prereqs (one-time)
1. `azd init` at repo root, choose existing Azure subscription, location (e.g. `eastus2`), env name.
2. Create GitHub federated identity in Entra and store secrets in repo: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
3. Flip `if: false` in `.github/workflows/deploy.yml` once smoke-tested locally with `azd up`.

## Local
```
dotnet run --project apps/AppHost
```
Aspire dashboard surfaces all service URLs and OTel traces.

## Smoke deploy from laptop
```
azd auth login
azd up
```
