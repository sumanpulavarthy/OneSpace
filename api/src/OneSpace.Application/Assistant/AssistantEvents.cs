using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.Application.Assistant;

/// <summary>Receives progress while an answer is produced (streaming).</summary>
public interface IAssistantEventSink
{
    ValueTask PublishAsync(AssistantStreamEvent streamEvent, CancellationToken cancellationToken);
}

/// <summary>A streaming event. <see cref="Type"/> is the SSE event name.</summary>
public abstract record AssistantStreamEvent(string Type);

/// <summary>Narrative text as it is generated. Provisional: <see cref="CompletedEvent"/> carries the final answer.</summary>
public sealed record TextDeltaEvent(string Text) : AssistantStreamEvent("delta");

/// <param name="Status">"started", or the outcome: "succeeded", "failed", "blocked", "connection_required".</param>
public sealed record ToolActivityEvent(string Tool, SourceSystem System, string Status) : AssistantStreamEvent("tool");

/// <summary>Discard the text streamed so far; the answer is being redone (e.g. escalated to a stronger model).</summary>
public sealed record RestartEvent(string Reason) : AssistantStreamEvent("restart");

public sealed record CompletedEvent(AssistantAnswer Answer) : AssistantStreamEvent("completed");

public sealed record ErrorEvent(string Message, string Code, string? TraceId) : AssistantStreamEvent("error");
