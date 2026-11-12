# FR-3 Hybrid retrieval

**Status:** Implemented with sample policies. Vector search built and tested (2026-11-12); not yet run against a real embedding model. ComplyKaro API pending Open Item 2.

| Data | Path | Store |
|---|---|---|
| Leave, holidays, shifts | Live Zoho MCP `tools/call` per question | none |
| Profile, calendar | Live Microsoft Graph (OBO) per question | none |
| Policy documents | Scheduled sync from ComplyKaro → search cache | Cosmos DB `policyDocuments` (text index) |
| Policy passages (embeddings) | Built from `policyDocuments` after each sync | Cosmos DB `policyChunks` (vector search) |

- Sync: `PolicySyncHostedService`, default every 24 h plus on startup (Open Item 3 decides the interval). An empty pull is treated as an outage and does not wipe the cache.
- **Keyword search:** `$text` index weighted title 5 / category 2 / content 1; best-matching paragraph returned as the snippet.
- **Vector search (`Retrieval` config):**
  - After each sync, `PolicyEmbeddingIndexer` splits each policy into passages and embeds them. Passages are about 800 characters, paragraphs are kept whole, and overlap starts at a sentence. The document title is prefixed to each passage.
  - Only documents whose content hash or embedding model changed are re-embedded. Chunks of deleted documents are removed. If indexing fails, keyword search still works.
  - The embedding model is any OpenAI-compatible one: OpenAI, Azure OpenAI or Ollama (`Retrieval:Embeddings`). Anthropic has no embeddings API.
  - `VectorStore=BruteForce` (default): exact cosine over vectors cached in the API. Works on any MongoDB, including the dev container; fine for thousands of chunks.
  - `VectorStore=CosmosVCore`: native `cosmosSearch` over an HNSW index created at startup. Needs `Embeddings:Dimensions`.
- **Hybrid ranking:** `HybridPolicySearch` combines keyword and vector results with reciprocal rank fusion (k=60). The snippet is the passage that matched by meaning. With no embedding model configured, or if the embedding call fails, it is plain keyword search.
- **Used by** the assistant's `search_policies` tool (the LLM gets the matching passages) and the GraphQL `searchPolicies` field.
- **Data class:** policy text and search queries are Internal, so the embedding provider's `MaxDataClass` must be at least Internal (checked at startup).
- `Integrations:ComplyKaro:Mode` = `Sample` (seed file) or `Api` (adjust `HttpComplyKaroSource` to the real contract).
- **Research, 2026-11-10 (Open Item 2):** complykaro.com presents ComplyKaro as a PoSH training and consultancy firm with a hosted e-learning LMS (`lms.complykaro.com`). The public site has no API, SSO/OAuth or policy-document repository. Expect ComplyKaro **not** to be a readable policy source. Likely options: (a) import the company's policy PDFs/exports through the same `IComplyKaroSource` interface, or (b) read policies from SharePoint via Graph. Ask ComplyKaro directly before Week 3.
- Generic words ("policy", "where", …) are stripped from the search text, and hits scoring under 50% of the top hit are dropped, so every document whose title contains "Policy" doesn't match.
- Target: < 1 s indexed search (NFR). Vector search adds one embedding call per query.

## Tests
- `OneSpace.UnitTests/Retrieval/RetrievalTests`: chunking, incremental indexing, stale removal, semantic hit the keyword search misses, fusion, keyword-only and failure fallback, embedder config.
- `OneSpace.IntegrationTests/PolicyRetrievalTests` (real MongoDB): index → brute-force vector search → hybrid; chunk replace and stale removal.
