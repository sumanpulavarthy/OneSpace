using System.Text.RegularExpressions;

namespace OneSpace.Assistant.Llm;

/// <summary>
/// Cost/latency routing: a single-domain lookup ("what's my leave balance?") goes to the fast model; anything
/// that spans domains, compares, explains or is long goes to the primary model. A fast answer that fails the
/// grounding check is escalated by the engine, so a wrong guess here costs latency, not correctness.
/// </summary>
public static partial class ModelRouter
{
    private const int MaxSimpleLength = 160;

    public static ModelTier Choose(string question, IReadOnlyCollection<string> domains, bool hasFastTier)
    {
        if (!hasFastTier) return ModelTier.Primary;
        if (domains.Count >= 2 || question.Length > MaxSimpleLength || Complex().IsMatch(question)) return ModelTier.Primary;
        return ModelTier.Fast;
    }

    [GeneratedRegex(@"\b(compare|comparison|versus|vs\.?|difference|differ|why|explain|plan|both|summari[sz]e|overlap|conflict|eligible|eligibility|calculate|should i)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Complex();
}
