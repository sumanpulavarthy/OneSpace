using OneSpace.Application.Assistant;
using OneSpace.Domain.Assistant;

namespace OneSpace.Api.GraphQL;

public sealed class Mutation
{
    /// <summary>
    /// Ask the assistant a question (FR-1). A mutation because it has side effects (query log) and must not be cached.
    /// </summary>
    public Task<AssistantAnswer> Ask(AskInput input, AskAssistantUseCase useCase, CancellationToken cancellationToken) =>
        useCase.ExecuteAsync(input.Question, input.SessionId, cancellationToken);
}

/// <param name="Question">Natural-language question, up to 2000 characters.</param>
/// <param name="SessionId">Omit to start a new conversation; pass back the returned id to continue it.</param>
public sealed record AskInput(string Question, string? SessionId);
