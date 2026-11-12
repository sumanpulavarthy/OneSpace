# ADR 0005: Grounded rendering and per-provider data classes

**Status:** Accepted (2026-11-12)

**Context:** We want MCP and Graph data shown in a form people can read: tables, cards and dates in their locale, not raw JSON. If the LLM writes those tables, it can produce a wrong figure in a neat table, and people trust tables more than prose. Separately, letting the assistant use any LLM means employee data can reach providers with different contractual and residency terms.

**Decision:**
- **Grounded rendering (FR-6).** `ToolResultPresenter` turns every tool result into sections. The employee sees them as blocks (`AnswerBlock`: table, key/value card, note) with canonical values. The LLM sees them as compact labelled facts. The LLM never writes block data. It refers to a result with a marker, `{{show:t1}}`, and the server swaps the marker for the blocks it built from that result. Markers are plain text, so they work with every provider.
- Presentation is schema-driven by `specs/mcp/presentation.json`, which sets labels, formats, ordering, visibility (`Both`/`Block`/`Model`) and data class per tool and field. Anything not listed is inferred: wrappers are unwrapped, maps become tables, dates, numbers and links are detected, and id fields are hidden. A new MCP tool can be presented without code. MCP `structuredContent` is used when the server sends it.
- **Numeric grounding check.** Every number in the narrative must appear in the evidence: tool values, derived totals, counts and date parts, the question, earlier turns, and today. Failures are reported on the answer (`grounding`), logged, counted in metrics, and shown to the employee as a note.
- **Data classes (FR-7).** Each tool and sensitive field has a class: Public, Internal, Personal or Confidential. Each provider has a `MaxDataClass`. The chain is only as trusted as its least-trusted provider. Tools above that level are neither offered nor executed. Fields above it are sent to the model as `[withheld]`, while the employee still sees them in the block. Mail and chat previews and leave reasons are Confidential, so they are masked for cloud providers (`Personal`) by default.
- Tool output reaches the model inside a `<tool_result>` envelope. Role tags, block markers and instruction-like text are neutralised first. This is defence in depth; the allowlist (ADR 0003) remains the hard control.

**Consequences:**
- The narrative stays short and the facts are exact. The model cannot summarise masked free text, such as the content of mail previews, unless a provider is trusted with Confidential data.
- The grounding check can flag a figure the model derived correctly but differently, such as a difference between two dates. This is acceptable: it shows a note and does not block the answer. Escalation, the metric and the eval keep it honest.
