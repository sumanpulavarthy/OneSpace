# ADR 0003: Server-side, deny-by-default tool allowlist

**Status:** Accepted (2026-11-10)

**Context:** The Zoho MCP server exposes about 95 tools, including writes such as `applyLeave` and `terminateEnrollment`. An LLM can be talked into calling any tool it can see, for example through prompt injection in tool output.

**Decision:** `specs/mcp/tool-allowlist.json` is the only source of callable tools. It is embedded into `OneSpace.Assistant` at build time and enforced by `ToolAllowlistGuard` in two places before any `tools/call`. The guard does not depend on the prompt, the UI or the model's tool list. Contract tests pin the active scope to the PRD and check that all Appendix A write and admin tools are denied.

**Consequences:** Widening scope means a reviewed spec change plus a contract-test change. Blocked attempts appear in the query log, which gives a cheap signal for injection attempts.
