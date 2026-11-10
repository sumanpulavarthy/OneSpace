namespace OneSpace.Domain.Assistant;

public sealed record AssistantAnswer(
    string SessionId,
    string Answer,
    string Intent,
    IReadOnlyList<SourceAttribution> Sources,
    IReadOnlyList<ToolInvocation> ToolCalls,
    long LatencyMs);
