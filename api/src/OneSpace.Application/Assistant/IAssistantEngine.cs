using OneSpace.Domain.Assistant;
using OneSpace.Domain.Users;

namespace OneSpace.Application.Assistant;

/// <summary>Resolves a natural-language question into tool calls and an answer. Implemented in OneSpace.Assistant.</summary>
public interface IAssistantEngine
{
    Task<AssistantEngineResult> AnswerAsync(UserContext user, string question, CancellationToken cancellationToken);
}

public sealed record AssistantEngineResult(
    string Answer,
    string Intent,
    IReadOnlyList<SourceAttribution> Sources,
    IReadOnlyList<ToolInvocation> ToolCalls);
