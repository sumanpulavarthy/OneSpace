using System.Security.Cryptography;
using System.Text;

namespace OneSpace.Application.Policies;

/// <summary>
/// Splits a policy into passages for embedding. Paragraphs are kept whole and packed up to the target size; a
/// paragraph longer than that is split at sentence (then word) boundaries with overlap, so no rule is cut in two
/// without context.
/// </summary>
public static class PolicyChunker
{
    public static IReadOnlyList<string> Split(string content, int size, int overlap)
    {
        size = Math.Max(size, 200);
        overlap = Math.Clamp(overlap, 0, size / 2);

        var paragraphs = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > size)
            {
                Flush();
                chunks.AddRange(SplitLong(paragraph, size, overlap));
                continue;
            }
            if (current.Length > 0 && current.Length + 2 + paragraph.Length > size) Flush();
            if (current.Length > 0) current.Append("\n\n");
            current.Append(paragraph);
        }
        Flush();
        return chunks;

        void Flush()
        {
            if (current.Length > 0) chunks.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>What is embedded: the title gives each passage its context ("Leave Policy: … cannot be carried forward").</summary>
    public static string EmbeddingInput(string title, string category, string chunk) => $"{title} ({category})\n{chunk}";

    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..32];

    private static IEnumerable<string> SplitLong(string paragraph, int size, int overlap)
    {
        var start = 0;
        while (start < paragraph.Length)
        {
            var end = Math.Min(start + size, paragraph.Length);
            if (end < paragraph.Length)
            {
                // Prefer a sentence end, then a space, in the second half of the window.
                var window = paragraph[start..end];
                var cut = window.LastIndexOfAny(['.', '!', '?', ';']);
                if (cut < size / 2) cut = window.LastIndexOf(' ');
                if (cut >= size / 2) end = start + cut + 1;
            }
            yield return paragraph[start..end].Trim();
            if (end >= paragraph.Length) yield break;

            // Start the overlap at a sentence inside the overlap window, else at a word boundary.
            var next = end - overlap;
            var sentence = paragraph.IndexOf(". ", next, Math.Max(0, end - 1 - next), StringComparison.Ordinal);
            var space = paragraph.IndexOf(' ', next);
            start = sentence >= 0 && sentence + 2 < end ? sentence + 2
                : space > next && space < end ? space + 1
                : Math.Max(next, start + 1);
        }
    }
}
