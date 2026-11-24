# ADR 0007: Observability with OpenTelemetry, Loki, Prometheus, Tempo and Grafana

**Status:** Proposed (2026-11-24), requested in architecture review (Rahul) — this stack is already used by CMART infrastructure.

**Context:** The POC uses Application Insights + Log Analytics via `Azure.Monitor.OpenTelemetry.AspNetCore`. Moving to the CMART Kubernetes platform, we should use the shared observability stack instead of paying for a separate one.

**Decision:**
- Instrument API and UI with the vendor-neutral OpenTelemetry SDK; export OTLP to an **OTel Collector** in the cluster.
- Collector routes **traces → Tempo**, **metrics → Prometheus** (or remote-write), **logs → Loki**.
- Dashboards and alerts in **Grafana**: 5xx rate, latency, blocked tool calls, LLM errors/tokens/cost, MongoDB health (exporter).
- Structured JSON logs with trace/span IDs so Grafana can jump between logs, traces and metrics.

**Consequences:**
- Replace `Azure.Monitor.OpenTelemetry.AspNetCore` with `OpenTelemetry.Extensions.Hosting` + OTLP exporter; existing OTel instrumentation stays.
- Remove App Insights / Log Analytics from Bicep.
- No PII in log messages or span attributes (user IDs hashed); retention set per the platform's policy.
