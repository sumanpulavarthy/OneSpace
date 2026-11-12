namespace OneSpace.Domain.Assistant;

/// <param name="Answer">The narrative, in plain text.</param>
/// <param name="Blocks">Tables and cards built from tool output (FR-6), in the order the narrative refers to them.</param>
/// <param name="Grounding">Whether every figure in <paramref name="Answer"/> was found in the tool data.</param>
public sealed record AssistantAnswer(
    string SessionId,
    string Answer,
    string Intent,
    IReadOnlyList<SourceAttribution> Sources,
    IReadOnlyList<ToolInvocation> ToolCalls,
    long LatencyMs,
    IReadOnlyList<AnswerBlock> Blocks,
    GroundingReport Grounding);
