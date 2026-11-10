namespace OneSpace.Domain.Assistant;

/// <summary>One tool call the assistant attempted while answering. Blocked calls are recorded too (FR-5).</summary>
public sealed record ToolInvocation(
    string Name,
    SourceSystem System,
    ToolInvocationOutcome Outcome,
    long DurationMs,
    string? Detail = null);

public enum ToolInvocationOutcome
{
    Succeeded,
    Failed,
    Blocked,

    /// <summary>The employee has not connected (or must reconnect) the source system.</summary>
    ConnectionRequired
}
