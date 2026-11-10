using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Tools;

/// <summary>
/// The tools the assistant may use, and their execution. Zoho tools are exposed only when they are on
/// the allowlist, and every Zoho call still goes through the guard inside <see cref="IZohoPeopleClient"/>.
/// </summary>
public sealed partial class AssistantToolbox(
    IZohoPeopleClient zoho,
    IMicrosoft365Client m365,
    IPolicyDocumentRepository policies,
    IToolAllowlistGuard guard,
    ILogger<AssistantToolbox> logger)
{
    public const string ZohoPrefix = "zoho_";
    public const string SearchPolicies = "search_policies";
    public const string GetMyProfile = "get_my_profile";
    public const string GetMyCalendar = "get_my_calendar";

    private const int MaxToolResultChars = 12_000;

    internal const string MpcDisabledMessage =
        "Zoho People's AI access (Zia/MCP) is turned off for your organisation, so OneSpace can't read Zoho data yet. " +
        "A Zoho People administrator needs to enable Zia in Zoho People Settings.";

    /// <summary>Tool definitions offered to the model for this request.</summary>
    public async Task<IReadOnlyList<AssistantToolDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken)
    {
        var tools = new List<AssistantToolDefinition>
        {
            new(SearchPolicies,
                "Search company policy documents synced from ComplyKaro (e.g. WFH, leave, travel, code of conduct). Returns titles and relevant excerpts.",
                Schema(("query", "string", "Keywords describing the policy", true))),
            new(GetMyProfile,
                "Get the signed-in employee's Microsoft 365 profile: name, email, job title, department, office.",
                Schema()),
            new(GetMyCalendar,
                "Get the signed-in employee's Outlook calendar entries between two ISO-8601 date-times to check availability.",
                Schema(("start", "string", "ISO-8601 start date-time", true), ("end", "string", "ISO-8601 end date-time", true)))
        };

        try
        {
            foreach (var tool in await zoho.ListAllowedToolsAsync(cancellationToken))
            {
                tools.Add(new(ZohoPrefix + tool.Name, $"Zoho People: {tool.Description}", tool.InputSchema));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The assistant can still answer policy/calendar questions while Zoho is unavailable.
            LogZohoUnavailable(logger, ex);
        }

        return tools;
    }

    public async Task<ToolExecution> ExecuteAsync(string toolName, JsonObject? input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var system = SystemFor(toolName);
        try
        {
            var (content, sources) = toolName switch
            {
                SearchPolicies => await SearchPoliciesAsync(GetString(input, "query"), cancellationToken),
                GetMyProfile => await GetProfileAsync(cancellationToken),
                GetMyCalendar => await GetCalendarAsync(GetDate(input, "start"), GetDate(input, "end"), cancellationToken),
                _ when toolName.StartsWith(ZohoPrefix, StringComparison.Ordinal) =>
                    await CallZohoAsync(toolName[ZohoPrefix.Length..], input, cancellationToken),
                // Unknown names are routed to the guard too, so the attempt is denied and recorded the same way.
                _ => await CallZohoAsync(toolName, input, cancellationToken)
            };

            return new ToolExecution(Truncate(content), false, sources,
                new ToolInvocation(toolName, system, ToolInvocationOutcome.Succeeded, stopwatch.ElapsedMilliseconds));
        }
        catch (ToolBlockedException ex)
        {
            LogToolBlocked(logger, ex.ToolName, ex.Reason);
            return new ToolExecution("This action is not available in OneSpace (read-only assistant).", true, [],
                new ToolInvocation(toolName, SourceSystem.ZohoPeople, ToolInvocationOutcome.Blocked, stopwatch.ElapsedMilliseconds, ex.Reason));
        }
        catch (ZohoConnectionRequiredException)
        {
            // Not a failure: the UI turns this outcome into a "Connect Zoho People" button.
            return new ToolExecution(
                "The employee has not connected Zoho People to OneSpace yet. Tell them to use the \"Connect Zoho People\" button shown with your answer, then ask again.",
                true, [],
                new ToolInvocation(toolName, SourceSystem.ZohoPeople, ToolInvocationOutcome.ConnectionRequired, stopwatch.ElapsedMilliseconds));
        }
        catch (SourceDisabledException ex)
        {
            LogToolFailed(logger, ex, toolName);
            return new ToolExecution(ex.Message, true, [],
                new ToolInvocation(toolName, system, ToolInvocationOutcome.Failed, stopwatch.ElapsedMilliseconds, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or JsonException or InvalidOperationException)
        {
            LogToolFailed(logger, ex, toolName);
            return new ToolExecution($"The {system.DisplayName()} lookup failed. Tell the employee it is temporarily unavailable.", true, [],
                new ToolInvocation(toolName, system, ToolInvocationOutcome.Failed, stopwatch.ElapsedMilliseconds, ex.GetType().Name));
        }
    }

    public static SourceSystem SystemFor(string toolName) => toolName switch
    {
        SearchPolicies => SourceSystem.ComplyKaro,
        GetMyProfile or GetMyCalendar => SourceSystem.Microsoft365,
        _ => SourceSystem.ZohoPeople
    };

    private async Task<(string, IReadOnlyList<SourceAttribution>)> CallZohoAsync(string mcpTool, JsonObject? input, CancellationToken ct)
    {
        // Pre-check here as well as in the client so a blocked call never costs a network round trip.
        var decision = guard.Authorize(mcpTool, input);
        if (!decision.Allowed) throw new ToolBlockedException(mcpTool, decision.Reason ?? "denied");

        var result = await zoho.CallToolAsync(mcpTool, decision.Arguments, ct);
        if (result.IsError && result.Text.Contains("MCP_DISABLED", StringComparison.Ordinal))
        {
            // An org-level setting, not an outage: say so, so the employee knows who can fix it.
            throw new SourceDisabledException(MpcDisabledMessage);
        }

        if (result.IsError)
        {
            // Zoho's error text explains what was wrong (e.g. a missing argument); keep it short in logs.
            throw new InvalidOperationException(
                $"Zoho MCP tool '{mcpTool}' returned an error: {(result.Text.Length > 300 ? result.Text[..300] : result.Text)}");
        }
        return (result.Text, [new SourceAttribution(SourceSystem.ZohoPeople, "From Zoho People", mcpTool)]);
    }

    private async Task<(string, IReadOnlyList<SourceAttribution>)> SearchPoliciesAsync(string query, CancellationToken ct)
    {
        var hits = await policies.SearchAsync(SearchTerms(query), 3, ct);
        if (hits.Count == 0) return ("No matching policy documents were found.", []);

        // Drop weak matches (e.g. a hit only on the word "policy") so they don't dilute the answer or its sources.
        var results = hits.Where(r => r.Score >= hits[0].Score * 0.5).ToList();

        var text = new StringBuilder();
        foreach (var r in results)
        {
            text.Append(CultureInfo.InvariantCulture, $"## {r.Document.Title} (category: {r.Document.Category}, updated {r.Document.SourceUpdatedAt:yyyy-MM-dd})\n");
            text.Append(r.Snippet).Append("\n\n");
        }

        var sources = results
            .Select(r => new SourceAttribution(SourceSystem.ComplyKaro, $"ComplyKaro policy: {r.Document.Title}", r.Document.SourceUrl ?? r.Document.Id))
            .ToList();
        return (text.ToString(), sources);
    }

    private async Task<(string, IReadOnlyList<SourceAttribution>)> GetProfileAsync(CancellationToken ct)
    {
        var profile = await m365.GetMyProfileAsync(ct);
        return (JsonSerializer.Serialize(profile), [new SourceAttribution(SourceSystem.Microsoft365, "From Microsoft 365 profile")]);
    }

    private async Task<(string, IReadOnlyList<SourceAttribution>)> GetCalendarAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (end <= start) throw new ArgumentException("end must be after start");
        if (end - start > TimeSpan.FromDays(31)) throw new ArgumentException("calendar range is limited to 31 days");

        var entries = await m365.GetMyCalendarAsync(start, end, ct);
        var text = entries.Count == 0 ? "No calendar entries in this range." : JsonSerializer.Serialize(entries);
        return (text, [new SourceAttribution(SourceSystem.Microsoft365, "From Outlook calendar")]);
    }

    private static readonly HashSet<string> GenericPolicyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "policy", "policies", "document", "documents", "rule", "rules", "where", "where's", "find", "show", "company", "our"
    };

    /// <summary>
    /// Removes words every policy shares (e.g. "policy" is in every title), which otherwise rank all documents alike.
    /// Falls back to the original text when nothing specific remains.
    /// </summary>
    internal static string SearchTerms(string query)
    {
        var specific = query
            .Split([' ', '?', '!', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !GenericPolicyWords.Contains(w.Trim('\'', '"')))
            .ToArray();
        return specific.Length == 0 ? query : string.Join(' ', specific);
    }

    private static string GetString(JsonObject? input, string name) =>
        input?[name]?.GetValue<string>() is { Length: > 0 } value ? value : throw new ArgumentException($"'{name}' is required");

    private static DateTimeOffset GetDate(JsonObject? input, string name) =>
        DateTimeOffset.TryParse(GetString(input, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : throw new ArgumentException($"'{name}' must be an ISO-8601 date-time");

    private static string Truncate(string text) =>
        text.Length <= MaxToolResultChars ? text : text[..MaxToolResultChars] + "\n[truncated]";

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties)
    {
        var props = new JsonObject();
        foreach (var p in properties) props[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray())
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Zoho MCP tools unavailable; continuing without them")]
    private static partial void LogZohoUnavailable(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked tool call {Tool}: {Reason}")]
    private static partial void LogToolBlocked(ILogger logger, string tool, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {Tool} failed")]
    private static partial void LogToolFailed(ILogger logger, Exception ex, string tool);
}

/// <summary>The source system is switched off by an administrator; the message is safe to show employees.</summary>
public sealed class SourceDisabledException(string message) : InvalidOperationException(message);

public sealed record AssistantToolDefinition(string Name, string Description, JsonObject InputSchema);

public sealed record ToolExecution(string Content, bool IsError, IReadOnlyList<SourceAttribution> Sources, ToolInvocation Invocation);
