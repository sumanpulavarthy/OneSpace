using OneSpace.Domain.Assistant;
using OneSpace.Domain.Users;

namespace OneSpace.Application.Assistant;

/// <summary>Resolves a natural-language question into tool calls and an answer. Implemented in OneSpace.Assistant.</summary>
public interface IAssistantEngine
{
    /// <param name="events">Receives progress (text deltas, tool activity) while answering; null when not streaming.</param>
    Task<AssistantEngineResult> AnswerAsync(UserContext user, AssistantRequest request, IAssistantEventSink? events, CancellationToken cancellationToken);
}

/// <param name="History">Earlier turns of this session, oldest first.</param>
public sealed record AssistantRequest(string Question, IReadOnlyList<ConversationTurn> History)
{
    public static AssistantRequest Single(string question) => new(question, []);
}

public sealed record AssistantEngineResult(
    string Answer,
    string Intent,
    IReadOnlyList<SourceAttribution> Sources,
    IReadOnlyList<ToolInvocation> ToolCalls)
{
    public IReadOnlyList<AnswerBlock> Blocks { get; init; } = [];
    public GroundingReport Grounding { get; init; } = GroundingReport.NotChecked;

    /// <summary>Which LLM answered and what it cost; null for the rule-based engine.</summary>
    public LlmUsage? Usage { get; init; }
}

public sealed record LlmUsage(
    string Provider,
    string Model,
    bool Escalated,
    bool FailedOver,
    long? InputTokens,
    long? OutputTokens);
