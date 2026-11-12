using System.Text.RegularExpressions;

namespace OneSpace.Assistant.Safety;

/// <summary>
/// Defence in depth against prompt injection through tool data (a leave reason, a mail subject, a policy).
/// Tool output reaches the LLM only inside a <c>tool_result</c> envelope the system prompt tells it to treat as
/// data; anything that could close the envelope, impersonate a role, fake a block marker or read like an
/// instruction is neutralised first. The authoritative control is still server-side: the allowlist guard.
/// </summary>
public static partial class ToolOutputSanitizer
{
    public const string RemovedInstruction = "[removed: instruction-like text]";

    public static string Wrap(string resultId, string toolName, string sourceLabel, string content, out bool flagged)
    {
        var clean = ControlChars().Replace(content, string.Empty);
        clean = RoleTag().Replace(clean, "[tag removed]");
        clean = clean.Replace("{{", "{ {", StringComparison.Ordinal).Replace("}}", "} }", StringComparison.Ordinal);

        flagged = Injection().IsMatch(clean);
        if (flagged) clean = Injection().Replace(clean, RemovedInstruction);

        return $"<tool_result id=\"{Attr(resultId)}\" tool=\"{Attr(toolName)}\" source=\"{Attr(sourceLabel)}\">\n{clean}\n</tool_result>";
    }

    private static string Attr(string value) => value.Replace("\"", "'", StringComparison.Ordinal).Replace("<", string.Empty, StringComparison.Ordinal);

    // Keeps \t, \n, \r.
    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F​-‏‪-‮⁦-⁩]")]
    private static partial Regex ControlChars();

    [GeneratedRegex(@"</?\s*(tool_result|tool_use|function_results?|system|assistant|user|human|instructions?)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex RoleTag();

    [GeneratedRegex(
        @"\b(ignore|disregard|forget|override)\s+(all\s+|any\s+|the\s+|your\s+)?(previous|prior|above|earlier|system)\s+(instructions?|prompts?|rules|messages?)" +
        @"|\byou\s+are\s+now\b|\bnew\s+instructions?\s*:|\bsystem\s+prompt\b|\bact\s+as\s+(an?\s+)?(admin|administrator|developer|system|root)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Injection();
}
