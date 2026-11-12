# ADR 0004: Provider-neutral LLM layer with an eval gate

**Status:** Accepted (2026-11-12)

**Context:** The assistant called the Anthropic Messages API directly (`ClaudeAssistantEngine`). We need to be able to use other LLMs (Azure OpenAI for data residency, Ollama for offline development) without rewriting the tool loop. But models differ a lot in tool-calling quality, and "any LLM" must not mean "any LLM, untested, with employee data".

**Decision:**
- `LlmAssistantEngine` runs the tool loop on `Microsoft.Extensions.AI.IChatClient`. Adapters: Anthropic's own (`AsIChatClient`), and the OpenAI SDK for OpenAI, Azure OpenAI (v1 endpoint) and Ollama (OpenAI-compatible endpoint).
- Providers are configured under `Assistant:Providers`, in failover order in `Assistant:Chain`. `ProviderChainChatClient` fails over to the next provider (before the first streamed token) and keeps a circuit breaker per provider.
- Each provider may set a `FastModel`. `ModelRouter` sends single-domain lookups to it. A fast answer that fails the grounding check (ADR 0005) is redone by the primary model.
- `specs/llm/models.json` is the capability matrix (tool calling, parallel calls, reasoning effort, prompt caching) and the **eval gate**. Outside Development the API refuses to start with a model whose status isn't `certified` (or `baseline`). Unsupported options (reasoning effort, parallel tool calls) are dropped per model.
- Certification is the golden-set eval (`api/tests/OneSpace.Evals`, `specs/evals`). It scores tool selection, grounding, required facts and an LLM-as-judge clarity rating against the thresholds in the catalog.
- LLM calls are traced with the GenAI OpenTelemetry conventions (model, tokens, latency; never contents).
- The legacy `Assistant:ApiKey`/`Assistant:Model` settings still work and become an Anthropic provider.

**Consequences:**
- Adding a model is a config change plus a reviewed catalog entry backed by an eval report.
- Anthropic's server-side refusal fallback beta is no longer used. Cross-provider failover replaces it, and a refusal maps to `ChatFinishReason.ContentFilter`.
- `claude-opus-5-5` is marked `baseline` (in production before the gate existed) and should be re-certified by running the eval.
