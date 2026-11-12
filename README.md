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
  src/OneSpace.Assistant       provider-neutral LLM tool loop, grounded rendering, allowlist guard
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
- `Assistant:Engine` is `RuleBased`, a keyword router, so no LLM key is needed (answers still show tables and cards). To use an LLM, set `Assistant__Engine=Llm` and a provider: see "LLM providers" below. The eval gate is off in Development.
- Policies come from `SeedData/policies.sample.json` (`Integrations:ComplyKaro:Mode=Sample`).
- Zoho and Microsoft 365 report "unavailable" until `Integrations:ZohoMcp:Endpoint` and the Graph app settings are set.

## LLM providers

The assistant uses any LLM behind `Microsoft.Extensions.AI` (ADR-0004). Tables and cards are built by the server from source data, never by the LLM (ADR-0005, FR-6).

```powershell
# Anthropic (default chain in appsettings.json)
dotnet user-secrets --project api/src/OneSpace.Api set "Assistant:ApiKey" "<key>"
# Azure OpenAI (endpoint without path gets /openai/v1/; Model = deployment name)
$env:Assistant__Chain__0 = 'azure'; $env:Assistant__Providers__azure__Type = 'AzureOpenAI'
$env:Assistant__Providers__azure__Endpoint = 'https://<resource>.openai.azure.com'
$env:Assistant__Providers__azure__Model = '<deployment>'; $env:Assistant__Providers__azure__CatalogModel = 'gpt-4.1'
$env:Assistant__Providers__azure__MaxDataClass = 'Personal'
# Ollama, offline (http://localhost:11434/v1/ by default)
$env:Assistant__Chain__0 = 'local'; $env:Assistant__Providers__local__Type = 'Ollama'; $env:Assistant__Providers__local__Model = 'qwen2.5:14b'
```

- **Data governance:** `MaxDataClass` (Public < Internal < Personal < Confidential, default Internal) caps what a provider may receive. Data classes per tool and field are in `specs/mcp/presentation.json`.
- **Failover and cost:** list several providers in `Assistant:Chain` for failover. Set `FastModel` for cheap single-domain lookups.
- **Eval gate:** outside Development every model must be `certified` in `specs/llm/models.json`. To certify one, run `$env:ONESPACE_EVALS='1'; dotnet test api/tests/OneSpace.Evals` with the provider configured, then record the passing report (`artifacts/evals/`) in the catalog.
- **Streaming:** `POST /assistant/stream` (SSE). The UI uses it and falls back to the `ask` mutation.

## Policy retrieval (keyword + vector)

Policy search is hybrid (FR-3): MongoDB `$text` plus embeddings, fused by rank. Embeddings are off by default (keyword only). To turn them on:

```powershell
# Local, free: Ollama with an embedding model
ollama pull nomic-embed-text
$env:Retrieval__Embeddings__Type = 'Ollama'; $env:Retrieval__Embeddings__Model = 'nomic-embed-text'
# or OpenAI / Azure OpenAI (Type, Model = deployment for Azure, Endpoint, ApiKey via user-secrets)
```

The next policy sync (on startup, or `POST /dev/sync-policies`) embeds the policies into `policyChunks`. Vector search is brute-force cosine in the API by default and works on any MongoDB. On Cosmos DB vCore, set `Retrieval:VectorStore=CosmosVCore` and `Retrieval:Embeddings:Dimensions` to use the native HNSW index.

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

- `Assistant--ApiKey` (Anthropic), or `Assistant--Providers--{name}--ApiKey` per provider
- `Integrations--ZohoMcp--Endpoint` (the server URL contains a key: `…/mcp/<key>/message`)
- `Integrations--Microsoft365--ClientSecret`
- `Database--ConnectionString`

Never put them in `appsettings*.json` or `.deploy/env`.

See [docs/architecture/implementation-plan.md](docs/architecture/implementation-plan.md) for the 4-week plan and open items.
