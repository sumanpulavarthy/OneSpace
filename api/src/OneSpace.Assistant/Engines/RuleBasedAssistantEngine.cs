using System.Text.Json.Nodes;
using OneSpace.Application.Assistant;
using OneSpace.Assistant.Tools;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Users;

namespace OneSpace.Assistant.Engines;

/// <summary>
/// Deterministic keyword router. Used for local development and demos without an LLM key, and as a
/// predictable baseline in tests. Uses the same toolbox (and therefore the same guard and the same
/// presentation) as the LLM engine, so tables and cards look the same without an LLM.
/// </summary>
public sealed class RuleBasedAssistantEngine(IAssistantToolbox toolbox, TimeProvider clock) : IAssistantEngine
{
    public async Task<AssistantEngineResult> AnswerAsync(UserContext user, AssistantRequest request, IAssistantEventSink? events, CancellationToken cancellationToken)
    {
        var question = request.Question;
        if (IsActionRequest(question))
        {
            return new AssistantEngineResult(
                "OneSpace is read-only for now, so I can't make changes like applying for or cancelling leave. " +
                "Please do that in Zoho People. I can tell you your balance or upcoming holidays first.",
                "action_declined", [], []);
        }

        var route = Route(question, clock.GetUtcNow());
        if (route is null)
        {
            return new AssistantEngineResult(
                "I can help with leave balances, holidays, shifts, company policies, your profile and your calendar. " +
                "Try \"What's my leave balance?\" or \"Where's the WFH policy?\"",
                "general", [], []);
        }

        var execution = await toolbox.ExecuteAsync(route.Value.Tool, route.Value.Input, "t1", cancellationToken);
        var blocks = execution.Presentation?.Blocks ?? [];
        var answer = execution.Invocation.Outcome switch
        {
            // The data is shown as blocks; text only when the source returned unstructured text.
            ToolInvocationOutcome.Succeeded when blocks.Count > 0 => route.Value.Lead,
            ToolInvocationOutcome.Succeeded => $"{route.Value.Lead}\n\n{(execution.Presentation?.RenderPlainText() ?? execution.Content).Trim()}",
            ToolInvocationOutcome.Blocked => "That isn't something OneSpace can do — it can only look information up.",
            ToolInvocationOutcome.ConnectionRequired => "Connect your Zoho People account so I can look that up for you, then ask again.",
            ToolInvocationOutcome.Failed when execution.Invocation.Detail == AssistantToolbox.MpcDisabledMessage => AssistantToolbox.MpcDisabledMessage,
            _ => $"{execution.Invocation.System.DisplayName()} isn't available right now, so I couldn't look that up. Please try again later."
        };
        return new AssistantEngineResult(answer, IntentClassifier.For([execution.Invocation]), execution.Sources, [execution.Invocation])
        {
            Blocks = execution.Invocation.Outcome == ToolInvocationOutcome.Succeeded ? blocks : []
        };
    }

    /// <summary>Requests to change something (PRD V2 scope) are declined before any lookup.</summary>
    internal static bool IsActionRequest(string question)
    {
        var q = question.ToLowerInvariant();
        string[] verbs = ["apply", "cancel", "book ", "raise", "submit", "approve", "reject", "withdraw", "request leave", "take leave", "mark me"];
        return verbs.Any(v => q.Contains(v, StringComparison.Ordinal))
            && !q.Contains("how do i", StringComparison.Ordinal) && !q.Contains("how to", StringComparison.Ordinal);
    }

    internal static (string Tool, JsonObject Input, string Lead)? Route(string question, DateTimeOffset now)
    {
        var q = question.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => q.Contains(w, StringComparison.Ordinal));

        // Zoho takes dates as dd-MMM-yyyy (from the live tool schemas).
        static string D(DateTimeOffset d) => d.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var today = now.Date;
        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = new DateTimeOffset(now.Year, 12, 31, 0, 0, 0, TimeSpan.Zero);

        if (Has("holiday"))
            return (AssistantToolbox.ZohoPrefix + "listholidays",
                new JsonObject { ["from"] = D(today), ["to"] = D(yearEnd), ["dateFormat"] = "dd-MMM-yyyy", ["upcoming"] = true },
                "Here are the upcoming holidays (Zoho People):");
        if (Has("leave type", "types of leave")) return (AssistantToolbox.ZohoPrefix + "fetchLeaveTypes", [], "Leave types available to you (Zoho People):");
        if (Has("who is on leave", "who's on leave", "team on leave", "out of office"))
            return (AssistantToolbox.ZohoPrefix + "getTeamMembersOnLeave", new JsonObject { ["from"] = D(today), ["to"] = D(today.AddDays(7)) },
                "Team members on leave this week (Zoho People):");
        if (Has("shift"))
            return (AssistantToolbox.ZohoPrefix + "getEmployeeShiftDetails",
                new JsonObject { ["sDate"] = D(today), ["eDate"] = D(today.AddDays(7)), ["dateFormat"] = "dd-MMM-yyyy" },
                "Your shifts for the next 7 days (Zoho People):");
        if (Has("leave record", "leave history", "leaves taken"))
            return (AssistantToolbox.ZohoPrefix + "getLeaveRecords", new JsonObject { ["from_date"] = D(yearStart), ["to_date"] = D(yearEnd) },
                "Your leave records this year (Zoho People):");
        if (Has("leave", "balance", "pto", "vacation")) return (AssistantToolbox.ZohoPrefix + "getLeaveBalance", [], "Your leave balance (Zoho People):");
        if (Has("calendar", "meeting", "free", "available", "availability"))
        {
            var start = now.Date;
            return (AssistantToolbox.GetMyCalendar,
                new JsonObject { ["start"] = start.ToString("O"), ["end"] = start.AddDays(7).ToString("O") },
                "Your calendar for the next 7 days (Outlook):");
        }
        if (Has("my profile", "my manager", "my department", "job title", "who am i")) return (AssistantToolbox.GetMyProfile, [], "Your Microsoft 365 profile:");
        if (Has("policy", "policies", "where is", "where's", "document", "rule", "allowed"))
            return (AssistantToolbox.SearchPolicies, new JsonObject { ["query"] = question }, "Policies that match your question (ComplyKaro):");

        return null;
    }
}
