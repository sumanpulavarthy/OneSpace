namespace OneSpace.Domain.Assistant;

/// <summary>
/// FR-4 query log: question, resolved intent, systems called and a response summary per user/session.
/// Support/debugging only — NOT the compliance audit trail required before Phase 1 (PRD Section 6).
/// </summary>
public sealed class QueryLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string UserId { get; set; }
    public required string SessionId { get; set; }
    public required string Question { get; set; }
    public string Intent { get; set; } = "unknown";
    public List<string> SystemsCalled { get; set; } = [];
    public List<ToolInvocation> ToolCalls { get; set; } = [];
    public string ResponseSummary { get; set; } = string.Empty;
    public bool Failed { get; set; }
    public long LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const int SummaryMaxLength = 500;

    public static string Summarize(string answer) =>
        answer.Length <= SummaryMaxLength ? answer : answer[..SummaryMaxLength] + "…";
}
