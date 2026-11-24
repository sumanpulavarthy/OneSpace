# ADR 0005: Kubernetes hosting and self-hosted MongoDB

**Status:** Proposed (2026-11-24), agreed in principle in architecture review (Rahul). Supersedes [ADR 0002](0002-cosmos-db-mongo-vcore.md).

**Context:** Review feedback: release on Kubernetes, use a dedicated database on K8s (HR data is sensitive; avoid cloud costs where possible), keep Key Vault, and use the existing CMART reverse proxy as for other apps. The POC targeted App Service, Static Web App and Cosmos DB for MongoDB vCore.

**Decision:**
- Ship the API (.NET 10) and UI (Angular, served by nginx) as container images, deployed with one Helm chart to namespaces `onespace-dev`, `onespace-test`, `onespace-prod` on the CMART cluster, behind the existing reverse proxy and ingress.
- Run **MongoDB** in the cluster as an operator-managed replica set (MongoDB Community or Percona operator), cluster-internal only. 1 node in dev, 3 in prod.
- Keep Azure Key Vault for secrets and the Data Protection key; pods access it via workload identity (AKS) or External Secrets Operator.

**Why MongoDB over PostgreSQL:**

| | MongoDB | PostgreSQL |
|---|---|---|
| Code change | None — Mongo driver, TTL and text indexes already used | Rewrite persistence (EF Core / Npgsql), migrations |
| Tests | Testcontainers.MongoDb already in place | New test setup |
| Data shape | Document-shaped (policy blocks, feed posts, query logs) | JSONB works but adds mapping |
| Vector search | MongoDB 8.2+ search (`mongot`), maturity to verify | pgvector, mature |
| Ops on K8s | Operator, well supported | Operator (CloudNativePG), well supported |

PostgreSQL remains a reasonable choice if the platform team already operates it and not MongoDB; the persistence layer sits behind `Application` ports, so the switch is contained to `OneSpace.Infrastructure`.

**Consequences:**
- OneSpace (or the platform team) owns DB HA, backups with point-in-time recovery, restore tests and upgrades that Cosmos handled.
- Azure Web PubSub is replaced by SignalR in the API (Redis backplane when scaled out); Front Door/WAF dropped for the POC.
- Bicep shrinks to Key Vault and Azure OpenAI; App Service, SWA and Cosmos modules are removed.
- Remove the vCore-specific setting `retryWrites=false` from connection strings.
