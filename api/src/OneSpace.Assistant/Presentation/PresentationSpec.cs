using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Presentation;

/// <summary>Where a field is shown: to the employee (block), to the LLM (model), or both.</summary>
public enum FieldView
{
    Both,
    Block,
    Model
}

/// <summary>specs/mcp/presentation.json (FR-6, FR-7), embedded at build time.</summary>
public sealed class PresentationSpec
{
    private const string ResourceName = "OneSpace.Assistant.presentation.json";

    public string Version { get; init; } = "1";
    public PresentationDefaults Defaults { get; init; } = new();
    public Dictionary<string, FieldSpec> FieldHints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ToolPresentation> Tools { get; init; } = new(StringComparer.Ordinal);

    public ToolPresentation? ForTool(string toolName) => Tools.GetValueOrDefault(toolName);

    /// <summary>Data class of a tool's output; unknown tools get the (conservative) default.</summary>
    public DataClass DataClassFor(string toolName) => ForTool(toolName)?.DataClass ?? Defaults.DataClass;

    /// <summary>Tool-specific field spec, then the global hint, matched case-insensitively.</summary>
    public FieldSpec? FieldFor(string toolName, string fieldName)
    {
        var tool = ForTool(toolName);
        if (tool?.Fields is { } fields)
        {
            foreach (var (name, spec) in fields)
                if (string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase)) return spec;
        }
        return FieldHints.GetValueOrDefault(fieldName);
    }

    public bool IsHidden(string toolName, string fieldName)
    {
        if (FieldFor(toolName, fieldName)?.Hidden is { } hidden) return hidden;
        if (Defaults.HiddenFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase)) return true;
        // Case-sensitive on purpose: "employeeId"/"leave_id" are identifiers, "Paid"/"valid" are not.
        return Defaults.HiddenSuffixes.Any(s => fieldName.Length > s.Length && fieldName.EndsWith(s, StringComparison.Ordinal));
    }

    public bool IsMeta(string fieldName) => Defaults.MetaFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);

    public static PresentationSpec LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded presentation spec '{ResourceName}' is missing.");
        return Parse(stream);
    }

    public static PresentationSpec Parse(Stream json)
    {
        var spec = JsonSerializer.Deserialize<PresentationSpec>(json, JsonOptions)
            ?? throw new InvalidOperationException("Presentation spec is empty.");
        // The deserializer builds its own dictionaries; re-key them so lookups are case-insensitive.
        return new PresentationSpec
        {
            Version = spec.Version,
            Defaults = spec.Defaults,
            FieldHints = new(spec.FieldHints, StringComparer.OrdinalIgnoreCase),
            Tools = new(spec.Tools, StringComparer.Ordinal)
        };
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class PresentationDefaults
{
    public DataClass DataClass { get; init; } = DataClass.Personal;
    public int MaxBlockRows { get; init; } = 50;
    public int MaxBlockColumns { get; init; } = 6;
    public int MaxModelRows { get; init; } = 100;
    public int MaxModelColumns { get; init; } = 12;
    public List<string> HiddenFields { get; init; } = [];
    public List<string> HiddenSuffixes { get; init; } = [];
    public List<string> MetaFields { get; init; } = [];
}

public sealed class ToolPresentation
{
    public string? Title { get; init; }

    /// <summary>Dot path to the part of the output to show, e.g. <c>data.0.leavetypes</c>. Ignored when it does not resolve.</summary>
    public string? Path { get; init; }
    public DataClass? DataClass { get; init; }
    public Dictionary<string, FieldSpec>? Fields { get; init; }
}

public sealed class FieldSpec
{
    public string? Label { get; init; }
    public FieldFormat? Format { get; init; }
    public bool? Hidden { get; init; }

    /// <summary>Free text written by people: classed <see cref="DataClass.Confidential"/> and masked for providers below that.</summary>
    public bool Sensitive { get; init; }

    /// <summary>Default: links are shown to the employee only (they cost tokens and can carry injected text); everything else to both.</summary>
    public FieldView? View { get; init; }
    public int Order { get; init; } = int.MaxValue;
}
