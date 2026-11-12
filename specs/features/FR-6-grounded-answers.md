# FR-6 Grounded, human-readable answers

**Status:** Implemented · **Contracts:** `specs/mcp/presentation.json`, `AssistantAnswer.blocks` / `grounding` in `specs/graphql/schema.graphql`

## Rules
- Every tool result is presented by the server. Employees see blocks (`TABLE`, `KEY_VALUE`, `TEXT`) built from the source data; the LLM never writes block data.
- The LLM writes 1–3 plain sentences and shows results with `{{show:<id>}}` markers. Markers are removed from the text. If the model uses none, every successful result is shown.
- Values are sent in canonical form (dates `yyyy-MM-dd`, date-times ISO 8601, invariant numbers). The UI formats them for the employee's locale.
- Tables are capped (`maxBlockRows`, `maxBlockColumns`) and report `totalRows`. Links are shown to the employee only.
- Every LLM answer passes the numeric grounding check. If it fails: `grounding.grounded=false`, `unverifiedValues` lists the figures, the UI shows a note, the query log records it, and a fast-model answer is redone by the primary model.
- Streaming (`POST /assistant/stream`, SSE): `delta`, `tool` and `restart` events, then `completed` with the exact GraphQL `AssistantAnswer` shape, or `error` with the GraphQL error codes.
- Follow-up questions use the last turns of the session (question and answer text only), kept for 24 hours and scoped to the employee.

## Tests
- `OneSpace.UnitTests/Presentation`: unwrap, map tables, masking, links, nested sections, raw text, totals.
- `OneSpace.UnitTests/Grounding`: grounding check, marker extraction, stream filter, sanitizer.
- `OneSpace.UnitTests/Engines/LlmAssistantEngineTests`: blocks, grounding, escalation, streaming, history.
- `OneSpace.ContractTests/AssistantSpecsContractTests`: embedded spec == file; every tool has a presentation; streamed enums and answer fields match the schema.
- `OneSpace.IntegrationTests/ConversationStoreTests`: per-employee session isolation and trimming.
- UI: `message-bubble.spec`, `format-value.spec`, `assistant.store.spec` (streaming, restart, fallback).
