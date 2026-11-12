using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Presentation;

/// <summary>One value, in canonical form (for blocks) and in the form the LLM sees.</summary>
internal sealed record Cell(string Canonical, string ModelText)
{
    public static Cell Empty { get; } = new(string.Empty, string.Empty);
    public bool IsEmpty => Canonical.Length == 0;
}

/// <summary>Infers and normalises value formats (dates, numbers, links) from raw tool JSON. Culture-invariant.</summary>
internal static partial class ValueFormatter
{
    private const int MaxCanonicalLength = 500;
    private const int MaxModelLength = 300;

    private static readonly string[] DateFormats =
    [
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "d MMMM yyyy", "dd-MMMM-yyyy"
    ];

    private static readonly string[] DateTimeFormats =
    [
        "dd-MMM-yyyy HH:mm", "dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy hh:mm tt", "dd-MMM-yyyy hh:mm:ss tt",
        "d-MMM-yyyy HH:mm", "d-MMM-yyyy hh:mm tt", "dd MMM yyyy HH:mm", "dd MMM yyyy hh:mm tt"
    ];

    /// <summary>The format a single value looks like, or null when it is empty.</summary>
    public static FieldFormat? Infer(string key, JsonNode? value)
    {
        if (value is null) return null;
        switch (value.GetValueKind())
        {
            case JsonValueKind.True or JsonValueKind.False:
                return FieldFormat.Boolean;
            case JsonValueKind.Number:
                return KeyHintsDate(key) && TryEpoch(value, out _) ? FieldFormat.DateTime : FieldFormat.Number;
            case JsonValueKind.String:
                var s = value.GetValue<string>().Trim();
                if (s.Length == 0) return null;
                if (IsLink(s)) return FieldFormat.Link;
                if (TryParseDate(s, out _, out var hasTime)) return hasTime ? FieldFormat.DateTime : FieldFormat.Date;
                if (IsNumber(s)) return FieldFormat.Number;
                if (bool.TryParse(s, out _)) return FieldFormat.Boolean;
                return FieldFormat.Text;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return null;
            default:
                return FieldFormat.Text;
        }
    }

    public static Cell Format(JsonNode? value, FieldFormat format)
    {
        if (value is null || value.GetValueKind() is JsonValueKind.Null or JsonValueKind.Undefined) return Cell.Empty;
        var cell = format switch
        {
            FieldFormat.Date => FormatDate(value, includeTime: false),
            FieldFormat.DateTime => FormatDate(value, includeTime: true),
            FieldFormat.Number => FormatNumber(value),
            FieldFormat.Boolean => FormatBoolean(value),
            FieldFormat.Bytes => FormatBytes(value),
            _ => null
        };
        return cell ?? FormatText(value);
    }

    /// <summary>"LastModifiedAt" → "Last modified at", "leave_type" → "Leave type".</summary>
    public static string Humanize(string key)
    {
        var text = new StringBuilder(key.Length + 8);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (c is '_' or '-' or '.') { text.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && (char.IsLower(key[i - 1]) || char.IsDigit(key[i - 1]))) text.Append(' ');
            text.Append(char.ToLowerInvariant(c));
        }
        var result = MultiSpace().Replace(text.ToString(), " ").Trim();
        return result.Length == 0 ? key : char.ToUpperInvariant(result[0]) + result[1..];
    }

    internal static bool TryParseDate(string s, out DateTimeOffset value, out bool hasTime)
    {
        value = default;
        hasTime = false;
        if (IsoDate().IsMatch(s))
        {
            hasTime = s.Length > 10;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
        }
        if (!char.IsDigit(s[0]) && !char.IsLetter(s[0])) return false;
        if (DateTimeOffset.TryParseExact(s, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            hasTime = true;
            return true;
        }
        return DateTimeOffset.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static Cell? FormatDate(JsonNode value, bool includeTime)
    {
        DateTimeOffset date;
        if (value.GetValueKind() == JsonValueKind.Number)
        {
            if (!TryEpoch(value, out date)) return null;
        }
        else if (value.GetValueKind() != JsonValueKind.String || !TryParseDate(value.GetValue<string>().Trim(), out date, out _))
        {
            return null;
        }

        return includeTime
            ? new Cell(date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                date.ToString("yyyy-MM-dd HH:mm 'UTC'zzz (ddd)", CultureInfo.InvariantCulture))
            : new Cell(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                date.ToString("yyyy-MM-dd (ddd)", CultureInfo.InvariantCulture));
    }

    private static Cell? FormatNumber(JsonNode value)
    {
        if (!TryDecimal(value, out var number)) return null;
        var text = number.ToString("0.############", CultureInfo.InvariantCulture);
        return new Cell(text, text);
    }

    private static Cell? FormatBoolean(JsonNode value)
    {
        bool? b = value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetValue<string>(), out var parsed) => parsed,
            _ => null
        };
        return b is null ? null : new Cell(b.Value ? "true" : "false", b.Value ? "yes" : "no");
    }

    private static Cell? FormatBytes(JsonNode value)
    {
        if (!TryDecimal(value, out var bytes)) return null;
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        var canonical = bytes.ToString("0", CultureInfo.InvariantCulture);
        return new Cell(canonical, unit == 0 ? $"{canonical} bytes" : $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}");
    }

    private static Cell FormatText(JsonNode value)
    {
        var text = value switch
        {
            JsonArray array => JoinArray(array),
            JsonObject obj => string.Join("; ", obj.Where(p => p.Value is JsonValue).Take(5).Select(p => $"{Humanize(p.Key)}: {Scalar(p.Value)}")),
            _ => Scalar(value)
        };
        text = Whitespace().Replace(text, " ").Trim();
        return new Cell(Truncate(text, MaxCanonicalLength), Truncate(text, MaxModelLength));
    }

    private static string JoinArray(JsonArray array)
    {
        var scalars = array.Where(n => n is JsonValue).Select(Scalar).Where(s => s.Length > 0).ToList();
        if (scalars.Count == array.Count) return string.Join(", ", scalars);
        return array.Count == 1 ? "1 item" : $"{array.Count} items";
    }

    private static string Scalar(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.String => node.GetValue<string>(),
        null or JsonValueKind.Null => string.Empty,
        _ => node.ToJsonString()
    };

    private static bool TryDecimal(JsonNode value, out decimal number)
    {
        number = 0;
        return value.GetValueKind() switch
        {
            JsonValueKind.Number => value.AsValue().TryGetValue(out number) || decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
            JsonValueKind.String => decimal.TryParse(value.GetValue<string>().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }

    private static bool TryEpoch(JsonNode value, out DateTimeOffset date)
    {
        date = default;
        if (!value.AsValue().TryGetValue<long>(out var n)) return false;
        if (n is >= 1_000_000_000 and < 10_000_000_000) { date = DateTimeOffset.FromUnixTimeSeconds(n); return true; }
        if (n is >= 1_000_000_000_000 and < 10_000_000_000_000) { date = DateTimeOffset.FromUnixTimeMilliseconds(n); return true; }
        return false;
    }

    private static bool KeyHintsDate(string key) =>
        key.Contains("date", StringComparison.OrdinalIgnoreCase) || key.Contains("time", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("At", StringComparison.Ordinal) || key.EndsWith("On", StringComparison.Ordinal);

    private static bool IsLink(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // Leading zeros ("007", phone numbers, codes) stay text so they are not reformatted.
    private static bool IsNumber(string s) => NumberPattern().IsMatch(s) && !(s.Length > 1 && s[0] == '0' && s[1] != '.');

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^-?\d+(\.\d+)?$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(" {2,}")]
    private static partial Regex MultiSpace();
}
