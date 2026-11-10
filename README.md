# OneSpace

One AI-searchable front door to Zoho People, ComplyKaro and Microsoft 365. The goal is to turn a 6-app HR errand into one question.
**Phase 0 (POC):** a read-only assistant for leave, holidays, shifts, policies, profile and calendar.

```
specs/            Source of truth: GraphQL schema, MCP tool allowlist, one spec per FR
api/              .NET 10 modular monolith (HotChocolate GraphQL)
  src/OneSpace.Api             host: GraphQL, Entra ID auth, DI
  src/OneSpace.Application     use cases + ports
  src/OneSpace.Domain          entities, value objects
  src/OneSpace.Infrastructure  Cosmos DB (Mongo API), user context, policy sync job
  src/OneSpace.Assistant       Claude tool loop, rule-based dev engine, allowlist guard
  src/OneSpace.Connectors      Zoho MCP (JSON-RPC), Microsoft Graph (OBO), ComplyKaro
  tests/                       Unit · Integration (Testcontainers) · Contract (vs /specs)
ui/               Angular 20, standalone + signals, MSAL, codegen from specs/graphql
.deploy/          Bicep (App Service, Static Web App, Key Vault, Cosmos vCore, App Insights)
.azuredevops/     CI: build, tests, spec-drift checks
docs/             ADRs, architecture, implementation plan
```

## Run locally

Prerequisites: .NET SDK 10, Node 22, Docker.

```powershell
./scripts/dev-up.ps1                                    # MongoDB on localhost:27018
$env:Database__ConnectionString = 'mongodb://localhost:27018'
dotnet run --project api/src/OneSpace.Api               # http://localhost:5256/graphql
cd ui; npm install; npm start                           # http://localhost:4200
```

In Development:
- The API accepts requests without a token as a fixed local user (`Authentication:AllowDevelopmentUser`). This never applies outside Development.
- `Assistant:Engine` is `RuleBased`, a keyword router, so no LLM key is needed. To use Claude, set `Assistant__Engine=Claude` and `Assistant__ApiKey` (user-secrets or an environment variable). The default model is `claude-opus-5-5` with server-side refusal fallback enabled.
- Policies come from `SeedData/policies.sample.json` (`Integrations:ComplyKaro:Mode=Sample`).
- Zoho and Microsoft 365 report "unavailable" until `Integrations:ZohoMcp:Endpoint` and the Graph app settings are set.

## Tests

```powershell
dotnet test api/OneSpace.sln      # unit + contract + integration (Docker needed)
cd ui; npm test                   # codegen + jest
```

## Changing the API (spec first)

1. Edit `specs/graphql/schema.graphql` (or the allowlist JSON / FR spec) and get it reviewed.
2. Implement it. Then run `./scripts/export-schema.ps1`. The contract test fails if the code and the spec differ.
3. Run `npm run codegen` in `ui/`. Any operation or type that no longer matches fails the UI build.

## Secrets

These live only in Key Vault, referenced by the App Service managed identity. `--` stands for `:`.

- `Assistant--ApiKey`
- `Integrations--ZohoMcp--Endpoint` (the server URL contains a key: `…/mcp/<key>/message`)
- `Integrations--Microsoft365--ClientSecret`
- `Database--ConnectionString`

Never put them in `appsettings*.json` or `.deploy/env`.

See [docs/architecture/implementation-plan.md](docs/architecture/implementation-plan.md) for the 4-week plan and open items.
