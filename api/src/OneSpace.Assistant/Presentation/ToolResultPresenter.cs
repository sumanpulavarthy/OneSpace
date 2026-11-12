using System.Text.Json;
using System.Text.Json.Nodes;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Presentation;

/// <summary>
/// Turns any tool output (MCP JSON, Graph results, policy hits) into sections: tables and key/value cards.
/// Schema-driven: <see cref="PresentationSpec"/> supplies labels, formats, visibility and data classes where it
/// knows the tool; everything else is inferred, so a new MCP tool is presentable without code.
/// </summary>
public sealed class ToolResultPresenter(PresentationSpec spec)
{
    private const int MaxDepth = 2;
    private const int MaxUnwrap = 5;

    public PresentationSpec Spec => spec;

    public PresentedResult Present(string toolName, string resultId, JsonNode? data, string rawText, SourceAttribution? source)
    {
        var toolClass = spec.DataClassFor(toolName);
        var title = spec.ForTool(toolName)?.Title ?? ValueFormatter.Humanize(StripPrefix(toolName));

        data ??= TryParse(rawText);
        var sections = data is null
            ? [Section.ForText(title, rawText, isRaw: true)]
            : Build(toolName, toolClass, Unwrap(data), title, 0);

        return new PresentedResult(toolName, resultId, sections, source, toolClass, spec.Defaults);
    }

    /// <summary>Descends through wrappers like <c>{"response":{"result":[...],"status":0}}</c> to the payload.</summary>
    internal JsonNode Unwrap(JsonNode node)
    {
        for (var i = 0; i < MaxUnwrap && node is JsonObject obj; i++)
        {
            var complex = obj.Where(p => p.Value is JsonObject or JsonArray).ToList();
            var scalarsAreMeta = obj.Where(p => p.Value is not (JsonObject or JsonArray)).All(p => spec.IsMeta(p.Key));
            if (complex.Count != 1 || !scalarsAreMeta) break;
            node = complex[0].Value!;
        }
        return node;
    }

    private List<Section> Build(string toolName, DataClass toolClass, JsonNode node, string title, int depth)
    {
        switch (node)
        {
            case JsonArray array:
                return [BuildTable(toolName, toolClass, title, array)];

            case JsonObject obj:
                var visible = obj.Where(p => !spec.IsHidden(toolName, p.Key)).ToList();
                if (visible.Count == 0) return [Section.ForText(title, "No details returned.")];

                // {"Casual Leave": {...}, "Sick Leave": {...}}: a map keyed by name reads best as a table.
                if (visible.Count >= 2 && visible.All(p => p.Value is JsonObject) && !visible.Any(p => spec.IsMeta(p.Key)))
                    return [BuildMapTable(toolName, toolClass, title, visible)];

                var complex = visible.Where(p => p.Value is JsonObject or JsonArray).ToList();
                var scalars = visible.Where(p => p.Value is not (JsonObject or JsonArray))
                    .Where(p => complex.Count == 0 || !spec.IsMeta(p.Key))
                    .ToList();

                var sections = new List<Section>();
                if (scalars.Count > 0)
                {
                    var row = new JsonObject(scalars.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
                    sections.Add(BuildCard(toolName, toolClass, title, row));
                }
                foreach (var (key, value) in complex)
                {
                    var label = spec.FieldFor(toolName, key)?.Label ?? ValueFormatter.Humanize(key);
                    if (depth < MaxDepth) sections.AddRange(Build(toolName, toolClass, value!, label, depth + 1));
                }
                return sections.Count > 0 ? sections : [Section.ForText(title, "No details returned.")];

            default:
                return [BuildCard(toolName, toolClass, title, new JsonObject { ["Value"] = node.DeepClone() })];
        }
    }

    private Section BuildTable(string toolName, DataClass toolClass, string title, JsonArray array)
    {
        if (array.Count == 0) return Section.ForText(title, "No records found.");

        var objects = array.OfType<JsonObject>().ToList();
        var rows = objects.Count > 0
            ? objects
            : array.Select(v => new JsonObject { ["Value"] = v?.DeepClone() }).ToList();
        return BuildRows(toolName, toolClass, title, SectionKind.Table, rows, array.Count);
    }

    private Section BuildMapTable(string toolName, DataClass toolClass, string title, List<KeyValuePair<string, JsonNode?>> entries)
    {
        var rows = entries.Select(e =>
        {
            var row = new JsonObject { ["Name"] = e.Key };
            foreach (var (k, v) in (JsonObject)e.Value!) row[k] = v?.DeepClone();
            return row;
        }).ToList();
        return BuildRows(toolName, toolClass, title, SectionKind.Table, rows, rows.Count);
    }

    private Section BuildCard(string toolName, DataClass toolClass, string title, JsonObject row) =>
        BuildRows(toolName, toolClass, title, SectionKind.KeyValue, [row], 1);

    private Section BuildRows(string toolName, DataClass toolClass, string title, SectionKind kind, List<JsonObject> rows, int totalRows)
    {
        var sample = rows.Take(spec.Defaults.MaxModelRows).ToList();
        var keys = new List<string>();
        foreach (var row in sample)
            foreach (var (key, _) in row)
                if (!keys.Contains(key, StringComparer.Ordinal) && !spec.IsHidden(toolName, key)) keys.Add(key);

        var columns = new List<Column>();
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var field = spec.FieldFor(toolName, key);
            var format = field?.Format ?? InferColumnFormat(key, sample.Select(r => r[key]));
            if (format is null) continue; // every value empty

            columns.Add(new Column(
                key,
                field?.Label ?? ValueFormatter.Humanize(key),
                format.Value,
                field?.View ?? (format == FieldFormat.Link ? FieldView.Block : FieldView.Both),
                field?.Sensitive == true ? DataClass.Confidential : toolClass,
                field?.Order ?? int.MaxValue,
                i));
        }
        columns = columns.OrderBy(c => c.Order).ThenBy(c => c.FirstSeen).ToList();

        var cells = sample.Select(r => columns.ToDictionary(c => c.Key, c => ValueFormatter.Format(r[c.Key], c.Format))).ToList();
        return new Section(title, kind, columns, cells, totalRows, null, false);
    }

    private static FieldFormat? InferColumnFormat(string key, IEnumerable<JsonNode?> values)
    {
        FieldFormat? format = null;
        foreach (var value in values)
        {
            var inferred = ValueFormatter.Infer(key, value);
            if (inferred is null) continue;
            if (format is null) format = inferred;
            else if (format != inferred) return FieldFormat.Text;
        }
        return format;
    }

    private static JsonNode? TryParse(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripPrefix(string toolName) =>
        toolName.StartsWith(Tools.AssistantToolbox.ZohoPrefix, StringComparison.Ordinal) ? toolName[Tools.AssistantToolbox.ZohoPrefix.Length..] : toolName;
}

internal enum SectionKind
{
    Text,
    Table,
    KeyValue
}

internal sealed record Column(string Key, string Label, FieldFormat Format, FieldView View, DataClass DataClass, int Order, int FirstSeen);

internal sealed record Section(
    string Title,
    SectionKind Kind,
    IReadOnlyList<Column> Columns,
    IReadOnlyList<Dictionary<string, Cell>> Rows,
    int TotalRows,
    string? Text,
    bool IsRawText)
{
    public static Section ForText(string title, string text, bool isRaw = false) =>
        new(title, SectionKind.Text, [], [], 0, text, isRaw);
}
