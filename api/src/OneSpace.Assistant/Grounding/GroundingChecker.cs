using System.Globalization;
using System.Text.RegularExpressions;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Grounding;

/// <summary>
/// FR-6 numeric grounding: every number in an LLM-written answer must appear in the evidence it was given
/// (tool data, derived totals and counts, the question, earlier turns, today's date). A number that does not is
/// reported as unverified. Cheap and deterministic, so it runs on every answer, not only in evals.
/// </summary>
public static partial class GroundingChecker
{
    private const int MaxReported = 10;

    public static GroundingReport Check(string answer, IEnumerable<string> evidence)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in evidence)
            foreach (var number in Numbers(text))
                allowed.Add(number);

        var unverified = Numbers(answer).Where(n => !allowed.Contains(n)).Distinct().Take(MaxReported).ToList();
        return new GroundingReport(true, unverified.Count == 0, unverified);
    }

    /// <summary>Numbers in normalised form: "1,200" → "1200", "4.50" → "4.5", "09" → "9".</summary>
    internal static IEnumerable<string> Numbers(string text)
    {
        foreach (Match match in NumberPattern().Matches(text))
        {
            var raw = match.Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                yield return value.ToString("0.############", CultureInfo.InvariantCulture);
        }
    }

    // Not preceded by a letter, digit or '.', so identifiers like "t1" or "v2.1" are not figures.
    [GeneratedRegex(@"(?<![\p{L}\d.])(\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}
