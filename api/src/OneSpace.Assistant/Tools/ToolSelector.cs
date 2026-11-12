namespace OneSpace.Assistant.Tools;

/// <summary>
/// Offers the model only the tools relevant to the question. Fewer tools means fewer tokens and fewer wrong
/// picks as the allowlist grows. Questions that match no domain get every tool, and a follow-up also gets the
/// tools used in the previous turn, so selection can narrow the choice but never strand a question.
/// </summary>
public static class ToolSelector
{
    private static readonly (string Domain, string[] Keywords, Func<string, bool> Tool)[] Domains =
    [
        ("leave",
            ["leave", "holiday", "vacation", "time off", "day off", "days off", "pto", "balance", "shift", "absent", "sick", "comp off", "compoff", "attendance", "on leave", "out of office"],
            t => t.StartsWith(AssistantToolbox.ZohoPrefix, StringComparison.Ordinal) || t == AssistantToolbox.SearchPolicies),
        ("policy",
            ["policy", "policies", "rule", "allowed", "allowance", "reimburs", "travel", "expense", "code of conduct", "handbook", "wfh", "work from home", "notice period", "entitle", "carry forward"],
            t => t == AssistantToolbox.SearchPolicies),
        ("calendar",
            ["calendar", "meeting", "free ", "busy", "availab", "schedule", "appointment"],
            t => t is AssistantToolbox.GetMyCalendar or AssistantToolbox.GetMyPresence),
        ("mail", ["mail", "email", "inbox", "message from"], t => t == AssistantToolbox.GetMyMail),
        ("files", ["file", "document", "onedrive", "sharepoint", "deck", "spreadsheet", "presentation"], t => t == AssistantToolbox.GetMyFiles),
        ("teams", ["teams", "channel", "chat"], t => t is AssistantToolbox.GetMyTeams or AssistantToolbox.GetMyChats),
        ("tasks", ["task", "to do", "todo", "to-do"], t => t == AssistantToolbox.GetMyTasks),
        ("people",
            ["manager", "report to", "reports", "direct report", "org", "profile", "job title", "title", "department", "office", "who am i", "my team", "presence", "status"],
            t => t is AssistantToolbox.GetMyProfile or AssistantToolbox.GetMyOrg or AssistantToolbox.GetMyPresence)
    ];

    public static IReadOnlySet<string> DomainsOf(string question)
    {
        var q = question.ToLowerInvariant();
        return Domains.Where(d => d.Keywords.Any(k => q.Contains(k, StringComparison.Ordinal))).Select(d => d.Domain).ToHashSet();
    }

    public static IReadOnlyList<AssistantToolDefinition> Select(
        IReadOnlyList<AssistantToolDefinition> tools, IReadOnlySet<string> domains, IReadOnlyCollection<string> previousTools)
    {
        if (domains.Count == 0) return tools;

        var matchers = Domains.Where(d => domains.Contains(d.Domain)).Select(d => d.Tool).ToList();
        var selected = tools.Where(t => matchers.Any(m => m(t.Name)) || previousTools.Contains(t.Name)).ToList();
        return selected.Count > 0 ? selected : tools;
    }
}
