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

    /// <summary>LLM provider and model that produced the answer (null for the rule-based engine).</summary>
    public string? Provider { get; set; }
    public string? Model { get; set; }

    /// <summary>The fast model's answer failed the grounding check and was redone by the primary model.</summary>
    public bool Escalated { get; set; }

    /// <summary>The first provider in the chain failed and another one answered.</summary>
    public bool FailedOver { get; set; }

    /// <summary>Null when not checked (rule-based engine).</summary>
    public bool? Grounded { get; set; }
    public List<string> UnverifiedValues { get; set; } = [];
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const int SummaryMaxLength = 500;

    public static string Summarize(string answer) =>
        answer.Length <= SummaryMaxLength ? answer : answer[..SummaryMaxLength] + "…";
}
