namespace OneSpace.Domain.Assistant;

/// <summary>
/// One question and answer in a session, kept briefly so follow-ups ("and next month?") have context.
/// Only the employee's question and the answer text are stored, never raw tool data.
/// </summary>
public sealed class ConversationTurn
{
    public required string Question { get; set; }
    public required string Answer { get; set; }

    /// <summary>Tools used for this turn; follow-up questions are offered the same tools.</summary>
    public List<string> ToolNames { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
