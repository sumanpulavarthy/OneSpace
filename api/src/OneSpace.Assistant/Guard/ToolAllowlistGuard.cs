using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OneSpace.Application.Integrations;

namespace OneSpace.Assistant.Guard;

/// <summary>
/// FR-5 allowlist guard, built from <c>specs/mcp/tool-allowlist.json</c> (embedded at build time).
/// Deny-by-default: a tool is callable only when named exactly in the spec's <c>allowed</c> list.
/// This never consults the prompt or the model — it is the last line before <c>tools/call</c>.
/// </summary>
public sealed class ToolAllowlistGuard : IToolAllowlistGuard
{
    public const string ResourceName = "OneSpace.Assistant.tool-allowlist.json";

    private readonly Dictionary<string, AllowedTool> _allowed;
    private readonly HashSet<string> _excludedAdminTools;

    public ToolAllowlistGuard(ToolAllowlistSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(spec);
        Spec = spec;
        _allowed = spec.Allowed.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _excludedAdminTools = new HashSet<string>(spec.ExcludedAdminReadTools, StringComparer.OrdinalIgnoreCase);
    }

    public ToolAllowlistSpec Spec { get; }

    public IReadOnlyCollection<string> AllowedTools => _allowed.Keys;

    public static ToolAllowlistGuard FromEmbeddedSpec() => new(LoadEmbeddedSpec());

    public static ToolAllowlistSpec LoadEmbeddedSpec()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded allowlist '{ResourceName}' is missing.");
        return Parse(stream);
    }

    public static ToolAllowlistSpec Parse(Stream json) =>
        JsonSerializer.Deserialize<ToolAllowlistSpec>(json, JsonOptions)
        ?? throw new InvalidOperationException("Tool allowlist spec is empty.");

    public ToolAuthorization Authorize(string toolName, JsonObject? arguments)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return ToolAuthorization.Deny("no tool name");

        if (_excludedAdminTools.Contains(toolName)) return ToolAuthorization.Deny("admin-scoped tool");

        // Exact, case-sensitive match: MCP tool names are case-sensitive and near-miss names must not slip through.
        if (!_allowed.TryGetValue(toolName, out var tool)) return ToolAuthorization.Deny("not on the allowlist");

        var effective = arguments is null ? new JsonObject() : (JsonObject)arguments.DeepClone();
        foreach (var (name, required) in tool.RequiredArguments)
        {
            if (effective[name] is JsonNode supplied &&
                !string.Equals(supplied.ToString(), required, StringComparison.OrdinalIgnoreCase))
            {
                return ToolAuthorization.Deny($"'{name}' must be '{required}' (self-scope only)");
            }

            effective[name] = required;
        }

        return new ToolAuthorization(true, null, effective);
    }

    /// <summary>Fails startup when the spec contradicts itself (e.g. a write verb on the allowlist).</summary>
    private static void Validate(ToolAllowlistSpec spec)
    {
        if (!string.Equals(spec.DefaultPolicy, "deny", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tool allowlist defaultPolicy must be 'deny'.");

        var problems = new List<string>();
        foreach (var tool in spec.Allowed)
        {
            if (spec.ExcludedAdminReadTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                problems.Add($"{tool.Name} is admin-scoped");
            if (spec.DeniedVerbPrefixes.Any(p => tool.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{tool.Name} starts with a write/admin verb");
        }

        if (spec.Allowed.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != spec.Allowed.Count)
            problems.Add("duplicate tool names");

        if (problems.Count > 0)
            throw new InvalidOperationException($"Tool allowlist spec is invalid: {string.Join("; ", problems)}.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

public sealed class ToolAllowlistSpec
{
    public string Version { get; init; } = string.Empty;
    public string System { get; init; } = string.Empty;
    public string DefaultPolicy { get; init; } = "deny";
    public List<AllowedTool> Allowed { get; init; } = [];
    public List<string> ExcludedAdminReadTools { get; init; } = [];
    public List<string> DeniedVerbPrefixes { get; init; } = [];
}

public sealed class AllowedTool
{
    public required string Name { get; init; }
    public string? Purpose { get; init; }

    /// <summary>Arguments forced to a fixed value, e.g. <c>data_select=MINE</c> for self-scope.</summary>
    public Dictionary<string, string> RequiredArguments { get; init; } = [];
}
