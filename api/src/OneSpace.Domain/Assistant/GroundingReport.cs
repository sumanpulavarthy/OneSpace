namespace OneSpace.Domain.Assistant;

/// <summary>
/// Result of checking an LLM-written answer against the tool data it was given (FR-6): every number in the
/// answer must appear in that data. <see cref="UnverifiedValues"/> lists the ones that did not.
/// </summary>
public sealed record GroundingReport(bool Checked, bool Grounded, IReadOnlyList<string> UnverifiedValues)
{
    /// <summary>Deterministic answers (rule-based engine) are not checked: they contain no generated figures.</summary>
    public static GroundingReport NotChecked { get; } = new(false, true, []);
}
