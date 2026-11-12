using System.Text.Json;
using System.Text.Json.Nodes;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Policies;

namespace OneSpace.Evals;

/// <summary>specs/evals: the golden set and the canned source data it runs against.</summary>
internal static class Specs
{
    // Declared first: static fields initialise in order and the ones below use it.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Root { get; } = FindRepoRoot();

    public static JsonObject Fixtures { get; } = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specs", "evals", "fixtures.json")))!;

    public static GoldenSet GoldenSet { get; } =
        JsonSerializer.Deserialize<GoldenSet>(File.ReadAllText(Path.Combine(Root, "specs", "evals", "golden-set.json")), Json)!;

    public static DateTimeOffset Today => DateTimeOffset.Parse(Fixtures["today"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);

    public static T Read<T>(JsonNode? node) => node.Deserialize<T>(Json)!;


    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "specs", "evals"))) return dir.FullName;
        throw new DirectoryNotFoundException("Could not find specs/evals.");
    }
}

internal sealed record GoldenSet(string Version, List<GoldenCase> Cases);

internal sealed record GoldenCase(string Id, string Question, List<string> ExpectTools, List<string> MustMention, List<string>? MustNotMention);

internal sealed class FixtureZoho(IToolAllowlistGuard guard) : IZohoPeopleClient
{
    private static JsonObject Zoho => Specs.Fixtures["zoho"]!.AsObject();

    public Task<IReadOnlyList<McpToolDescriptor>> ListAllowedToolsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpToolDescriptor>>(Zoho["tools"]!.AsArray()
            .Select(t => new McpToolDescriptor(t!["name"]!.GetValue<string>(), t["description"]?.GetValue<string>(), t["inputSchema"]!.DeepClone().AsObject()))
            .Where(t => guard.AllowedTools.Contains(t.Name))
            .ToList());

    public Task<McpToolResult> CallToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
    {
        var decision = guard.Authorize(toolName, arguments);
        if (!decision.Allowed) throw new ToolBlockedException(toolName, decision.Reason ?? "denied");
        var result = Zoho["results"]![toolName] ?? throw new InvalidOperationException($"No fixture for {toolName}");
        return Task.FromResult(new McpToolResult(result.ToJsonString(), false));
    }
}

internal sealed class FixtureMicrosoft365 : IMicrosoft365Client
{
    private static JsonNode? M365(string key) => Specs.Fixtures["microsoft365"]![key];

    public Task<EmployeeProfile> GetMyProfileAsync(CancellationToken cancellationToken) => Task.FromResult(Specs.Read<EmployeeProfile>(M365("profile")));

    public Task<IReadOnlyList<CalendarEntry>> GetMyCalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarEntry>>(Specs.Read<List<CalendarEntry>>(M365("calendar")).Where(e => e.End > from && e.Start < to).ToList());

    public Task<EmployeeProfile?> GetMyManagerAsync(CancellationToken cancellationToken) => Task.FromResult<EmployeeProfile?>(Specs.Read<EmployeeProfile>(M365("manager")));
    public Task<IReadOnlyList<EmployeeProfile>> GetMyDirectReportsAsync(CancellationToken cancellationToken) => List<EmployeeProfile>("directReports");
    public Task<PresenceStatus> GetMyPresenceAsync(CancellationToken cancellationToken) => Task.FromResult(Specs.Read<PresenceStatus>(M365("presence")));
    public Task<IReadOnlyList<MailMessageSummary>> GetMyMessagesAsync(string? search, int top, CancellationToken cancellationToken) => List<MailMessageSummary>("mail");
    public Task<IReadOnlyList<DriveItemSummary>> GetMyFilesAsync(string? search, int top, CancellationToken cancellationToken) => List<DriveItemSummary>("files");
    public Task<IReadOnlyList<TeamSummary>> GetMyTeamsAsync(CancellationToken cancellationToken) => List<TeamSummary>("teams");
    public Task<IReadOnlyList<ChatSummary>> GetMyChatsAsync(int top, CancellationToken cancellationToken) => List<ChatSummary>("chats");
    public Task<IReadOnlyList<TodoTaskSummary>> GetMyTasksAsync(int top, CancellationToken cancellationToken) => List<TodoTaskSummary>("tasks");

    private static Task<IReadOnlyList<T>> List<T>(string key) => Task.FromResult<IReadOnlyList<T>>(Specs.Read<List<T>>(M365(key)));
}

/// <summary>Keyword search over the fixture policies (the real repository uses Mongo $text).</summary>
internal sealed class FixturePolicies : IPolicyDocumentRepository, IPolicySearch
{
    private static readonly List<PolicyDocument> Documents = Specs.Read<List<PolicyDocument>>(Specs.Fixtures["policies"]);

    public Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken)
    {
        var terms = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2).ToList();
        var hits = Documents
            .Select(d => (Doc: d, Score: (double)terms.Count(t => (d.Title + " " + d.Content).Contains(t, StringComparison.OrdinalIgnoreCase))))
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .Take(limit)
            .Select(h => new PolicySearchResult(h.Doc, h.Score, h.Doc.Content))
            .ToList();
        return Task.FromResult<IReadOnlyList<PolicySearchResult>>(hits);
    }

    public Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken) => Task.FromResult(0L);
    public Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));
    public Task<IReadOnlyList<PolicyDocument>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PolicyDocument>>(Documents);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
