using OneSpace.Assistant.Tools;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Engines;

/// <summary>FR-4 intent label, derived from the tools an answer used.</summary>
public static class IntentClassifier
{
    public static string For(IReadOnlyList<ToolInvocation> calls)
    {
        var intents = calls
            .Select(c => c.Name switch
            {
                AssistantToolbox.SearchPolicies => "policy_lookup",
                AssistantToolbox.GetMyCalendar => "calendar_lookup",
                AssistantToolbox.GetMyProfile => "profile_lookup",
                var n when n.Contains("Leave", StringComparison.OrdinalIgnoreCase) => "leave_lookup",
                var n when n.Contains("holiday", StringComparison.OrdinalIgnoreCase) => "holiday_lookup",
                _ => "hr_lookup"
            })
            .Distinct()
            .ToList();
        return intents.Count == 0 ? "general" : string.Join('+', intents);
    }
}
