# ADR 0002: Azure Cosmos DB for MongoDB (vCore)

**Status:** Accepted (2026-11-10)

**Context:** The brief names Cosmos DB, while the PRD names MongoDB + Atlas Search. Phase 0 needs two collections: query logs with a TTL, and a policy cache with text search (vector search later).

**Decision:** Use Azure Cosmos DB for MongoDB vCore, accessed through the official MongoDB .NET driver.

**Why:** It keeps the Mongo driver, local Mongo containers and Testcontainers. It supports `$text`, TTL indexes and vector search. It is Azure-native, sits beside Key Vault and App Service, and needs no Atlas account.

**Consequences:** Locally and in CI the tests run against `mongo:7.0`, so any feature used must be supported by vCore (checked: text indexes, TTL, bulk upsert). Retryable writes are off in the vCore connection string.
