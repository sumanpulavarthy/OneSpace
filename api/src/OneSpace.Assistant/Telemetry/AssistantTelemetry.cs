using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OneSpace.Assistant.Telemetry;

/// <summary>
/// Traces and metrics for the assistant. LLM calls themselves are traced by Microsoft.Extensions.AI's
/// OpenTelemetry client (GenAI semantic conventions: model, tokens, latency) under <see cref="LlmSourceName"/>.
/// Prompt and response contents are never recorded.
/// </summary>
public static class AssistantTelemetry
{
    public const string SourceName = "OneSpace.Assistant";
    public const string LlmSourceName = "OneSpace.Assistant.Llm";

    public static readonly ActivitySource Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> Answers = Meter.CreateCounter<long>("onespace.assistant.answers", description: "Answers produced, by provider, model and tier");
    public static readonly Counter<long> GroundingFailures = Meter.CreateCounter<long>("onespace.assistant.grounding_failures", description: "Answers with figures not found in tool data");
    public static readonly Counter<long> Escalations = Meter.CreateCounter<long>("onespace.assistant.escalations", description: "Fast-model answers redone by the primary model");
    public static readonly Counter<long> Failovers = Meter.CreateCounter<long>("onespace.assistant.provider_failovers", description: "LLM calls that failed over to the next provider");
    public static readonly Counter<long> InjectionFlags = Meter.CreateCounter<long>("onespace.assistant.injection_flags", description: "Tool outputs containing instruction-like text");
    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("onespace.assistant.tool_calls", description: "Tool calls by tool and outcome");
    public static readonly Histogram<long> ToolDuration = Meter.CreateHistogram<long>("onespace.assistant.tool_duration", unit: "ms");
}
