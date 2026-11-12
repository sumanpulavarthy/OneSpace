using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.Application.Assistant;

/// <summary>FR-1 + FR-4: answers a question for the current employee (with session context) and logs the query.</summary>
public sealed class AskAssistantUseCase(
    IUserContextAccessor userContext,
    IAssistantEngine engine,
    IQueryLogRepository queryLog,
    IConversationStore conversations,
    ILogger<AskAssistantUseCase> logger)
{
    public const int MaxQuestionLength = 2000;

    /// <summary>Turns of history offered to the engine; it trims further to its character budget.</summary>
    public const int MaxHistoryTurns = 6;

    public Task<AssistantAnswer> ExecuteAsync(string question, string? sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(question, sessionId, null, cancellationToken);

    public async Task<AssistantAnswer> ExecuteAsync(string question, string? sessionId, IAssistantEventSink? events, CancellationToken cancellationToken)
    {
        question = question?.Trim() ?? string.Empty;
        if (question.Length == 0) throw new ArgumentException("Ask a question.", nameof(question));
        if (question.Length > MaxQuestionLength)
            throw new ArgumentException($"Questions are limited to {MaxQuestionLength} characters.", nameof(question));

        var user = userContext.Current;
        var isNewSession = string.IsNullOrWhiteSpace(sessionId);
        sessionId = isNewSession ? Guid.NewGuid().ToString("N") : sessionId!.Trim();
        var stopwatch = Stopwatch.StartNew();

        var entry = new QueryLogEntry { UserId = user.ObjectId, SessionId = sessionId, Question = question };
        try
        {
            var history = isNewSession ? [] : await TryGetHistoryAsync(user.ObjectId, sessionId, cancellationToken);
            var result = await engine.AnswerAsync(user, new AssistantRequest(question, history), events, cancellationToken);
            stopwatch.Stop();

            entry.Intent = result.Intent;
            entry.ToolCalls = [.. result.ToolCalls];
            entry.SystemsCalled = result.ToolCalls.Select(t => t.System.DisplayName()).Distinct().ToList();
            entry.ResponseSummary = QueryLogEntry.Summarize(result.Answer);
            entry.LatencyMs = stopwatch.ElapsedMilliseconds;
            entry.Provider = result.Usage?.Provider;
            entry.Model = result.Usage?.Model;
            entry.Escalated = result.Usage?.Escalated ?? false;
            entry.FailedOver = result.Usage?.FailedOver ?? false;
            entry.InputTokens = result.Usage?.InputTokens;
            entry.OutputTokens = result.Usage?.OutputTokens;
            entry.Grounded = result.Grounding.Checked ? result.Grounding.Grounded : null;
            entry.UnverifiedValues = [.. result.Grounding.UnverifiedValues];

            await TryAppendTurnAsync(user.ObjectId, sessionId, new ConversationTurn
            {
                Question = question,
                Answer = result.Answer,
                ToolNames = result.ToolCalls.Select(c => c.Name).Distinct().ToList()
            });

            return new AssistantAnswer(sessionId, result.Answer, result.Intent, result.Sources, result.ToolCalls, entry.LatencyMs,
                result.Blocks, result.Grounding);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry.Failed = true;
            entry.ResponseSummary = ex.GetType().Name;
            entry.LatencyMs = stopwatch.ElapsedMilliseconds;
            throw;
        }
        finally
        {
            await TryLogAsync(entry);
        }
    }

    private async Task<IReadOnlyList<ConversationTurn>> TryGetHistoryAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return await conversations.GetRecentTurnsAsync(userId, sessionId, MaxHistoryTurns, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without history a follow-up is answered as a fresh question, which is better than failing it.
            logger.LogWarning(ex, "Failed to read conversation history for session {SessionId}", sessionId);
            return [];
        }
    }

    private async Task TryAppendTurnAsync(string userId, string sessionId, ConversationTurn turn)
    {
        try
        {
            await conversations.AppendAsync(userId, sessionId, turn, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save conversation turn for session {SessionId}", sessionId);
        }
    }

    private async Task TryLogAsync(QueryLogEntry entry)
    {
        try
        {
            // Not tied to the request token: a cancelled request should still leave a log entry.
            await queryLog.AddAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Logging must never fail the employee's answer.
            logger.LogWarning(ex, "Failed to write query log for session {SessionId}", entry.SessionId);
        }
    }
}
