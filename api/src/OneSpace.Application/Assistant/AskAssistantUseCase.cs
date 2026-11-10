using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.Application.Assistant;

/// <summary>FR-1 + FR-4: answers a question for the current employee and logs the query.</summary>
public sealed class AskAssistantUseCase(
    IUserContextAccessor userContext,
    IAssistantEngine engine,
    IQueryLogRepository queryLog,
    ILogger<AskAssistantUseCase> logger)
{
    public const int MaxQuestionLength = 2000;

    public async Task<AssistantAnswer> ExecuteAsync(string question, string? sessionId, CancellationToken cancellationToken)
    {
        question = question?.Trim() ?? string.Empty;
        if (question.Length == 0) throw new ArgumentException("Ask a question.", nameof(question));
        if (question.Length > MaxQuestionLength)
            throw new ArgumentException($"Questions are limited to {MaxQuestionLength} characters.", nameof(question));

        var user = userContext.Current;
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();
        var stopwatch = Stopwatch.StartNew();

        var entry = new QueryLogEntry { UserId = user.ObjectId, SessionId = sessionId, Question = question };
        try
        {
            var result = await engine.AnswerAsync(user, question, cancellationToken);
            stopwatch.Stop();

            entry.Intent = result.Intent;
            entry.ToolCalls = [.. result.ToolCalls];
            entry.SystemsCalled = result.ToolCalls.Select(t => t.System.DisplayName()).Distinct().ToList();
            entry.ResponseSummary = QueryLogEntry.Summarize(result.Answer);
            entry.LatencyMs = stopwatch.ElapsedMilliseconds;

            return new AssistantAnswer(sessionId, result.Answer, result.Intent, result.Sources, result.ToolCalls, entry.LatencyMs);
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
