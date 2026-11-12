# FR-7 LLM provider governance

**Status:** Implemented · **Contracts:** `specs/llm/models.json`, `specs/mcp/presentation.json` (data classes), `specs/evals/*`

## Rules
- Any provider behind `IChatClient` may be configured: Anthropic, OpenAI, Azure OpenAI, Ollama (`Assistant:Providers`, order in `Assistant:Chain`).
- **Eval gate:** outside Development, every configured model must be in `specs/llm/models.json` with `toolCalling: true` and status `certified` or `baseline`, or the API does not start.
- **Certification:** run `ONESPACE_EVALS=1 dotnet test api/tests/OneSpace.Evals` with the provider configured. If the report in `artifacts/evals/` meets the catalog thresholds, set the model's status to `certified` with the report name and date in a reviewed PR.
- **Data classes:** a provider's `MaxDataClass` (default `Internal`) caps what it may receive. The chain uses the lowest value across its providers. Tools above it are not offered or executed; fields above it are sent as `[withheld]`.
- **Resilience:** failover to the next provider on error (before the first streamed token), with a circuit breaker per provider (`Assistant:CircuitBreaker`).
- **Cost:** single-domain lookups use the provider's `FastModel` when configured. Only tools relevant to the question are offered. The stable system prompt is cached where the provider supports it.
- **Observability:** OpenTelemetry sources `OneSpace.Assistant` and `OneSpace.Assistant.Llm` (GenAI conventions; no prompt or response contents). The query log records provider, model, tokens, failover, escalation and grounding.

## Tests
- `OneSpace.UnitTests/Llm/ProviderChainTests`: failover, breaker, tiers, option adaptation, data-class minimum, validation, eval gate, legacy config, endpoints, routing.
- `OneSpace.ContractTests/AssistantSpecsContractTests.Production_configuration_passes_the_eval_gate`.
- `OneSpace.Evals`: the golden set (LLM, opt-in) and its fixtures (always).
