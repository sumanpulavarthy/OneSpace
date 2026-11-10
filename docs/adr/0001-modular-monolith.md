# ADR 0001: Modular monolith on one App Service

**Status:** Accepted (2026-11-10)

**Context:** 2 developers, 4 weeks. cmart-accord splits Gateway, Platform and Notification into separate services. That split costs deploy and operations time this team can't spare.

**Decision:** One deployable API with layered projects: `Api` (host, GraphQL, auth), `Application` (use cases, ports), `Domain`, `Infrastructure` (Cosmos/Mongo, identity, jobs), `Assistant` (LLM orchestration, allowlist guard), `Connectors` (Zoho MCP, Graph, ComplyKaro). HotChocolate is the gateway. There is no `Shared` project and no Notification service in Phase 0.

**Consequences:** One pipeline and one App Service. Project references enforce the boundaries (`Application` depends on nothing outward). Later-phase features can become their own modules, or services if needed, without changing the GraphQL contract.
