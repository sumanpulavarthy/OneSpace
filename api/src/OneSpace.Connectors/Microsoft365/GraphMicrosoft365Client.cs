using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;

namespace OneSpace.Connectors.Microsoft365;

public sealed class Microsoft365Options
{
    public const string SectionName = "Integrations:Microsoft365";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string? TenantId { get; set; }

    /// <summary>The OneSpace API app registration (same one the UI requests tokens for).</summary>
    public string? ClientId { get; set; }

    /// <summary>Key Vault reference only. Prefer a certificate / managed identity federated credential in higher envs.</summary>
    public string? ClientSecret { get; set; }

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";

    /// <summary>
    /// Delegated, read-only Graph permissions. Grant them (with admin consent) on the app registration
    /// so the on-behalf-of exchange never needs an interactive prompt.
    /// </summary>
    public string[] Scopes { get; set; } =
    [
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Calendars.Read",
        "https://graph.microsoft.com/Mail.Read",
        "https://graph.microsoft.com/Files.Read.All",
        "https://graph.microsoft.com/Team.ReadBasic.All",
        "https://graph.microsoft.com/Chat.Read",
        "https://graph.microsoft.com/Tasks.Read",
        "https://graph.microsoft.com/Presence.Read"
    ];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Exchanges the employee's inbound token for a Graph token (on-behalf-of), so Graph enforces their own permissions.</summary>
public sealed class OnBehalfOfTokenProvider
{
    private readonly Microsoft365Options _options;
    private readonly Lazy<IConfidentialClientApplication> _app;

    public OnBehalfOfTokenProvider(IOptions<Microsoft365Options> options)
    {
        _options = options.Value;
        _app = new Lazy<IConfidentialClientApplication>(() => ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority(new Uri(new Uri(_options.Instance), _options.TenantId))
            .Build());
    }

    public async Task<string> GetGraphTokenAsync(string userAssertion, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) throw new InvalidOperationException("Microsoft 365 integration is not configured.");

        // MSAL caches OBO tokens in memory keyed by the assertion; nothing is persisted.
        var result = await _app.Value
            .AcquireTokenOnBehalfOf(_options.Scopes, new UserAssertion(userAssertion))
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }
}

/// <summary>Read-only Graph client. Every request targets <c>/me</c>, so it can only return the caller's own data.</summary>
public sealed partial class GraphMicrosoft365Client(
    HttpClient http,
    OnBehalfOfTokenProvider tokens,
    IUserContextAccessor userContext) : IMicrosoft365Client
{
    private const string PersonSelect = "$select=displayName,mail,userPrincipalName,jobTitle,department,officeLocation";
    private const int MaxTodoLists = 10;

    public async Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync($"me?{PersonSelect}", cancellationToken);
        return ToProfile(doc!.RootElement);
    }

    public async Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var query = string.Create(CultureInfo.InvariantCulture,
            $"me/calendarView?startDateTime={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}&endDateTime={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}&$select=subject,start,end,showAs,location&$orderby=start/dateTime&$top=50");
        using var doc = await GetAsync(query, cancellationToken, preferUtc: true);

        return Values(doc!).Select(e => new CalendarEntry(
                Str(e, "subject") ?? "(no subject)",
                ParseGraphDate(e.GetProperty("start")),
                ParseGraphDate(e.GetProperty("end")),
                Str(e, "showAs") ?? "unknown",
                e.TryGetProperty("location", out var loc) ? Str(loc, "displayName") : null))
            .ToList();
    }

    public async Task<EmployeeProfile?> GetMyManagerAsync(CancellationToken cancellationToken)
    {
        // 404 means no manager is set in Entra ID.
        using var doc = await GetAsync($"me/manager?{PersonSelect}", cancellationToken, allowNotFound: true);
        return doc is null ? null : ToProfile(doc.RootElement);
    }

    public async Task<IReadOnlyList<EmployeeProfile>> GetMyDirectReportsAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync($"me/directReports?{PersonSelect}&$top=100", cancellationToken);
        return Values(doc!).Select(ToProfile).ToList();
    }

    public async Task<PresenceStatus> GetMyPresenceAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync("me/presence", cancellationToken);
        var p = doc!.RootElement;
        return new PresenceStatus(Str(p, "availability") ?? "PresenceUnknown", Str(p, "activity") ?? "PresenceUnknown");
    }

    public async Task<IReadOnlyList<MailMessageSummary>> GetMyMessagesAsync(string? search, int top, CancellationToken cancellationToken)
    {
        const string select = "$select=subject,from,receivedDateTime,bodyPreview,isRead,importance,webLink";
        var n = Clamp(top);
        // Graph doesn't allow $orderby with $search; search results come back by relevance.
        var query = string.IsNullOrWhiteSpace(search)
            ? $"me/messages?{select}&$orderby=receivedDateTime desc&$top={n}"
            : $"me/messages?{select}&$search={Uri.EscapeDataString($"\"{search.Replace("\"", string.Empty, StringComparison.Ordinal)}\"")}&$top={n}";
        using var doc = await GetAsync(query, cancellationToken);

        return Values(doc!).Select(m => new MailMessageSummary(
                Str(m, "subject") ?? "(no subject)",
                m.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var addr)
                    ? Str(addr, "name") ?? Str(addr, "address")
                    : null,
                ParseDate(Str(m, "receivedDateTime")) ?? DateTimeOffset.MinValue,
                Str(m, "bodyPreview"),
                m.TryGetProperty("isRead", out var read) && read.ValueKind == JsonValueKind.True,
                Str(m, "importance") ?? "normal",
                Str(m, "webLink")))
            .ToList();
    }

    public async Task<IReadOnlyList<DriveItemSummary>> GetMyFilesAsync(string? search, int top, CancellationToken cancellationToken)
    {
        var n = Clamp(top);
        var query = string.IsNullOrWhiteSpace(search)
            ? $"me/drive/recent?$top={n}"
            // OData string literal: single quotes are escaped by doubling.
            : $"me/drive/root/search(q='{Uri.EscapeDataString(search.Replace("'", "''", StringComparison.Ordinal))}')?$top={n}";
        using var doc = await GetAsync(query, cancellationToken);

        return Values(doc!).Take(n).Select(item =>
            {
                // Items shared from other drives carry their details in remoteItem.
                var source = item.TryGetProperty("remoteItem", out var remote) ? remote : item;
                return new DriveItemSummary(
                    Str(item, "name") ?? Str(source, "name") ?? "(unnamed)",
                    Str(item, "webUrl") ?? Str(source, "webUrl"),
                    ParseDate(Str(item, "lastModifiedDateTime") ?? Str(source, "lastModifiedDateTime")),
                    source.TryGetProperty("lastModifiedBy", out var by) && by.TryGetProperty("user", out var user) ? Str(user, "displayName") : null,
                    source.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
                    source.TryGetProperty("folder", out _));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TeamSummary>> GetMyTeamsAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync("me/joinedTeams?$select=displayName,description", cancellationToken);
        return Values(doc!).Select(t => new TeamSummary(Str(t, "displayName") ?? "(unnamed team)", Str(t, "description"))).ToList();
    }

    public async Task<IReadOnlyList<ChatSummary>> GetMyChatsAsync(int top, CancellationToken cancellationToken)
    {
        var n = Clamp(top, max: 50);
        using var doc = await GetAsync(
            $"me/chats?$expand=lastMessagePreview&$orderby=lastMessagePreview/createdDateTime desc&$top={n}", cancellationToken);

        return Values(doc!).Select(c =>
            {
                string? from = null, preview = null;
                DateTimeOffset? at = null;
                if (c.TryGetProperty("lastMessagePreview", out var last) && last.ValueKind == JsonValueKind.Object)
                {
                    at = ParseDate(Str(last, "createdDateTime"));
                    if (last.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object &&
                        f.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
                    {
                        from = Str(u, "displayName");
                    }
                    if (last.TryGetProperty("body", out var body)) preview = PlainText(Str(body, "content"));
                }
                return new ChatSummary(Str(c, "topic"), Str(c, "chatType") ?? "unknown", from, preview, at, Str(c, "webUrl"));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TodoTaskSummary>> GetMyTasksAsync(int top, CancellationToken cancellationToken)
    {
        var n = Clamp(top, max: 50);
        using var lists = await GetAsync($"me/todo/lists?$select=id,displayName&$top={MaxTodoLists}", cancellationToken);

        var tasks = new List<TodoTaskSummary>();
        foreach (var list in Values(lists!).Take(MaxTodoLists))
        {
            var id = Str(list, "id");
            if (id is null) continue;
            var listName = Str(list, "displayName") ?? "Tasks";

            using var doc = await GetAsync(
                $"me/todo/lists/{Uri.EscapeDataString(id)}/tasks?$filter=status ne 'completed'&$select=title,status,importance,dueDateTime&$top={n}",
                cancellationToken);
            tasks.AddRange(Values(doc!).Select(t => new TodoTaskSummary(
                Str(t, "title") ?? "(untitled)",
                listName,
                Str(t, "status") ?? "notStarted",
                Str(t, "importance") ?? "normal",
                t.TryGetProperty("dueDateTime", out var due) && due.ValueKind == JsonValueKind.Object ? ParseGraphDate(due) : null)));
        }

        // Dated tasks first (soonest due), then undated.
        return tasks.OrderBy(t => t.DueAt is null).ThenBy(t => t.DueAt).Take(n).ToList();
    }

    private async Task<JsonDocument?> GetAsync(string relativeUrl, CancellationToken cancellationToken, bool preferUtc = false, bool allowNotFound = false)
    {
        var assertion = userContext.Current.AccessToken
            ?? throw new InvalidOperationException("No user token is available for on-behalf-of exchange.");
        var token = await tokens.GetGraphTokenAsync(assertion, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (preferUtc) request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");

        using var response = await http.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static JsonElement.ArrayEnumerator Values(JsonDocument doc) => doc.RootElement.GetProperty("value").EnumerateArray();

    private static EmployeeProfile ToProfile(JsonElement e) => new(
        Str(e, "displayName") ?? string.Empty,
        Str(e, "mail") ?? Str(e, "userPrincipalName"),
        Str(e, "jobTitle"),
        Str(e, "department"),
        Str(e, "officeLocation"));

    private static int Clamp(int top, int max = 25) => Math.Clamp(top, 1, max);

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static DateTimeOffset ParseGraphDate(JsonElement dateTimeTimeZone) =>
        DateTimeOffset.Parse(Str(dateTimeTimeZone, "dateTime")!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Teams message previews are HTML; the assistant only needs the text.</summary>
    private static string? PlainText(string? html) =>
        html is null ? null : WebUtility.HtmlDecode(HtmlTag().Replace(html, " ")).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();
}
