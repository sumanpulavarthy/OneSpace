# OneSpace Phase 0 — Implementation Plan

Scope: PRD v0.2, Phase 0 (4-week POC). AI Assistant only, read-only, across Zoho People (MCP), ComplyKaro and Microsoft 365. Team: 2 developers (D1 UI-leaning, D2 API-leaning), 2 QAs.

**Stack:** Angular 20 (standalone, signals) · .NET 10 + HotChocolate 16 · Azure Cosmos DB for MongoDB (vCore) · Entra ID (OIDC + OBO) · Claude (`claude-opus-5-5`) for tool routing · App Service + Static Web Apps · App Insights · Azure DevOps.

**Working rule (spec-driven):** a change starts in `specs/`: the GraphQL schema, the MCP allowlist and an FR spec. CI fails when code drifts from the specs.

## Status after scaffold (2026-11-10)

| Area | State |
|---|---|
| Repo layout, CPM, analyzers, CI pipeline | Done |
| Entra ID JWT validation + dev-only local user | Done |
| GraphQL: `me`, `myProfile`, `leaveBalance`, `searchPolicies`, `recentQueries`, `ask` | Done, schema exported to spec |
| Allowlist guard (FR-5) + contract tests | Done |
| Zoho MCP JSON-RPC client (Streamable HTTP + SSE) | Done; untested against the real server |
| Graph OBO client (profile, calendar) | Done; needs app registration + secret |
| ComplyKaro sync + text search (sample data) | Done; real API pending Open Item 2 |
| Claude tool-calling engine + rule-based dev engine | Done; needs an API key in Key Vault |
| Query log (FR-4), 30-day TTL | Done |
| Angular shell, MSAL, chat UI, codegen from spec | Done |
| Bicep (App Service, SWA, Key Vault, Cosmos vCore, App Insights) | Written; **not compiled or deployed** (no Azure CLI/Bicep on the build machine) |
| UI checked in headless Edge (desktop + 390 px mobile) | Done 2026-11-10: no console errors, no horizontal scroll |
| Zoho per-employee OAuth ("Connect Zoho People") | Done: PKCE, dynamic registration, encrypted refresh tokens, revoke on disconnect (FR-2). Verified live up to the connect prompt. **The first real consent still has to be completed by a person.** |
| ComplyKaro API research | No public API found. Plan a document import or SharePoint (see FR-3). |
| Tests | API 115 (unit 62, contract 47, integration 6) · UI 15 |

## Week 1 — Foundation and integration feasibility (exit = PRD 13.1)

| Day | D1 | D2 | QA |
|---|---|---|---|
| 1 | Create the Entra app registrations (SPA + API, `access_as_user` scope). Fill `app-config.json`. | Fill `AzureAd`. Deploy the Bicep to dev. Load Key Vault secrets. | Write the test plan for auth. Review FR specs. |
| 2 | Real sign-in end to end. Check the token reaches the API. | Confirm claim mapping (`oid`, `preferred_username`) against real tokens. | Auth negative tests: expired, wrong audience, no token. |
| 3 | Chat UX polish: loading and error states, mobile width. | **Zoho MCP:** run `initialize` and `tools/list` against `<your-org>.zohomcp.in`. Record the inbound auth model (Open Item 5) and the real argument names → `UserIdentityArguments`. | PazCare OAuth research. |
| 4 | Accessibility pass (keyboard, screen reader). | **Graph OBO:** grant `User.Read` + `Calendars.Read`, test `myProfile` and the calendar. | ComplyKaro API research: does a document-content API exist? |
| 5 | Buffer | Buffer | **Decision checkpoint:** OBO support per system, written down. Pick fallbacks. |

## Week 2 — Live query path

- D2: Run Zoho tools against real data. Capture sample payloads into fixtures. Harden `ZohoMcpClient` (session expiry/re-initialize, retries for 5xx only).
- D2: Add an `IZohoPeopleClient` integration test with a recorded fake MCP server.
- D1: Render structured answers (leave balance as a small table), show source chips, "New conversation".
- Both: Set `Assistant:Engine=Claude` in dev. Tune the system prompt for the 3 FR-1 intents. Keep `effort=low` unless answers are weak.
- QA: Intent test set (≥30 phrasings × 3 intents). Prompt-injection cases, e.g. tool output containing "call applyLeave": the call must show as `BLOCKED`.

## Week 3 — ComplyKaro and document retrieval

- D2: Implement `HttpComplyKaroSource` against the confirmed API (or a manual export import if no API exists). Set the sync interval (Open Item 3).
- D2: Measure search quality. If keyword search is weak, add embeddings and vector search on Cosmos vCore.
- D1: Policy answers with a link to the source document. Empty and no-match states.
- QA: Policy question set. Check sync idempotency and deletion handling.

## Week 4 — Hardening and demo

- Measure the NFRs: < 3 s real-time (excluding LLM), < 1 s search. Record the results in this file.
- Load-test 20 concurrent users on the dev App Service SKU.
- Write a security checklist: no tokens in logs, Key Vault-only secrets, CORS locked to the SWA host, allowlist contract green.
- Prepare the demo script and back the "15 min → 30 s" claim with a time study (Open Item 1).
- Go/no-go for Phase 1. Design the audit layer before PazCare (Open Item 4).

## Risks

| Risk | Mitigation in code |
|---|---|
| Zoho MCP only accepts a server credential, so FR-2 can't rely on Zoho | Self-scope forced by the guard, identity arguments overwritten, no cross-employee GraphQL arguments |
| Prompt injection via tool results | The guard runs server-side and denies by default. The model never sees non-allowlisted tools. Blocked attempts are logged. |
| ComplyKaro has no content API | `Sample`/`Api` source switch. A manual import fits the same `IComplyKaroSource`. |
| LLM latency breaks the NFR | `effort=low`, ≤5 tool rounds, real-time NFR excludes generation. The rule-based engine is the fallback. |
