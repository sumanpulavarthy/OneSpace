# FR-3 Hybrid retrieval

**Status:** Implemented with sample policies; ComplyKaro API pending Open Item 2

| Data | Path | Store |
|---|---|---|
| Leave, holidays, shifts | Live Zoho MCP `tools/call` per question | none |
| Profile, calendar | Live Microsoft Graph (OBO) per question | none |
| Policy documents | Scheduled sync from ComplyKaro → search cache | Cosmos DB `policyDocuments` (text index) |

- Sync: `PolicySyncHostedService`, default every 24 h plus on startup (Open Item 3 decides the interval). An empty pull is treated as an outage and does not wipe the cache.
- Search: `$text` index weighted title 5 / category 2 / content 1; best-matching paragraph returned as the snippet.
- Upgrade path: vector search on Cosmos DB for MongoDB vCore if keyword relevance is too weak.
- `Integrations:ComplyKaro:Mode` = `Sample` (seed file) or `Api` (adjust `HttpComplyKaroSource` to the real contract).
- **Research, 2026-11-10 (Open Item 2):** complykaro.com presents ComplyKaro as a PoSH training and consultancy firm with a hosted e-learning LMS (`lms.complykaro.com`). The public site has no API, SSO/OAuth or policy-document repository. Expect ComplyKaro **not** to be a readable policy source. Likely options: (a) import the company's policy PDFs/exports through the same `IComplyKaroSource` interface, or (b) read policies from SharePoint via Graph. Ask ComplyKaro directly before Week 3.
- Generic words ("policy", "where", …) are stripped from the search text, and hits scoring under 50% of the top hit are dropped, so every document whose title contains "Policy" doesn't match.
- Target: < 1 s indexed search (NFR).
