# ADR 0006: OpenAI models as the LLM provider

**Status:** Proposed (2026-11-24), requested in architecture review (Rahul): "use a Copilot model or OpenAI instead of Claude". Exact provider to confirm.

**Context:** The POC assistant uses `ClaudeAssistantEngine` (Anthropic SDK) behind the `IAssistantEngine` port, with `RuleBasedAssistantEngine` as fallback. Tool safety is enforced server-side by `ToolAllowlistGuard` ([ADR 0003](0003-server-side-tool-allowlist.md)), independent of the model.

**Decision:** Add an `OpenAiAssistantEngine` adapter using OpenAI models with function calling, hosted on **Azure OpenAI** (recommended) or the OpenAI API. Select the engine by configuration (`Assistant:Engine`). Keep the rule-based fallback.

**Options considered:**
- *Azure OpenAI (recommended):* same GPT model family that Microsoft Copilot uses; Entra/managed-identity auth, chosen Azure region, customer data not used for training; billed to the existing Azure subscription.
- *OpenAI API:* same models, simpler setup; data processed outside our Azure tenancy.
- *GitHub Copilot:* licensed for developer tooling, not as a backend for other applications.
- *Microsoft 365 Copilot:* would mean building OneSpace as a Copilot agent/plugin rather than calling a model; needs M365 Copilot licences for every user. Possible later as an extra channel.

**Consequences:**
- No change to the GraphQL contract, guard, tool allowlist or UI.
- The guard/contract/integration test suite runs against every engine; cut-over only after parity on the assistant test set.
- Add token/cost metrics and alerts (Grafana).
- Data-residency and HR-data approval moves from Anthropic terms to the Azure OpenAI region.
