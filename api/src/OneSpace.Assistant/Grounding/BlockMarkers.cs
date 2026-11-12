using System.Text;
using System.Text.RegularExpressions;

namespace OneSpace.Assistant.Grounding;

/// <summary>
/// The LLM shows a tool result by writing <c>{{show:t1}}</c>; the server replaces the marker with blocks it
/// built from that result's data (FR-6). Markers are the one provider-neutral way to do this: every model can
/// write text, not every model supports structured output alongside tool calls.
/// </summary>
public static partial class BlockMarkers
{
    public static string For(string resultId) => $"{{{{show:{resultId}}}}}";

    /// <summary>The answer without markers, and the result ids it referred to, in order.</summary>
    public static (string Text, IReadOnlyList<string> Ids) Extract(string text)
    {
        var ids = Marker().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        return (Clean(Remove(text)), ids);
    }

    internal static string Remove(string text) => Marker().Replace(text, string.Empty);

    private static string Clean(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd());
        return BlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"\{\{\s*show\s*:\s*([A-Za-z0-9_.\-]+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}

/// <summary>Removes markers from streamed text, holding back a possibly incomplete marker until it closes.</summary>
public sealed class MarkerStreamFilter
{
    private const int MaxHeld = 40;
    private readonly StringBuilder _pending = new();

    public string Append(string delta)
    {
        _pending.Append(delta);
        var text = BlockMarkers.Remove(_pending.ToString());
        _pending.Clear();

        var hold = PossibleMarkerStart(text);
        if (hold < 0) return text;

        var held = text[hold..];
        if (held.Length > MaxHeld) return text; // not a marker after all
        _pending.Append(held);
        return text[..hold];
    }

    public string Flush()
    {
        var text = BlockMarkers.Remove(_pending.ToString());
        _pending.Clear();
        return text;
    }

    private static int PossibleMarkerStart(string text)
    {
        var i = text.LastIndexOf('{');
        if (i < 0) return -1;
        var start = i > 0 && text[i - 1] == '{' ? i - 1 : i;
        return text.IndexOf("}}", start, StringComparison.Ordinal) < 0 ? start : -1;
    }
}
