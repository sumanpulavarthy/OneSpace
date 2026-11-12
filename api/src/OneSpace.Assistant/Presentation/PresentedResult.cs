using System.Globalization;
using System.Text;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Presentation;

/// <summary>
/// One tool result in its two views: <see cref="Blocks"/> for the employee (full, canonical values) and
/// <see cref="RenderForModel"/> for the LLM (compact facts, masked by data class).
/// </summary>
public sealed class PresentedResult
{
    internal const string Withheld = "[withheld]";

    private readonly IReadOnlyList<Section> _sections;
    private readonly SourceAttribution? _source;
    private readonly PresentationDefaults _defaults;
    private IReadOnlyList<AnswerBlock>? _blocks;

    internal PresentedResult(string toolName, string resultId, IReadOnlyList<Section> sections, SourceAttribution? source,
        DataClass dataClass, PresentationDefaults defaults)
    {
        ToolName = toolName;
        ResultId = resultId;
        DataClass = dataClass;
        _sections = sections;
        _source = source;
        _defaults = defaults;
    }

    public string ToolName { get; }

    /// <summary>The id the LLM uses to show this result (<c>{{show:t1}}</c>).</summary>
    public string ResultId { get; }

    public DataClass DataClass { get; }

    /// <summary>Blocks for the employee. Raw, non-JSON text is summarised by the LLM rather than shown.</summary>
    public IReadOnlyList<AnswerBlock> Blocks => _blocks ??= BuildBlocks();

    /// <summary>True when the result is unstructured text (nothing to show as a block).</summary>
    public bool IsUnstructured => _sections.All(s => s.IsRawText);

    /// <summary>The facts the LLM sees. Fields classed above <paramref name="maxDataClass"/> are replaced by <c>[withheld]</c>.</summary>
    public string RenderForModel(DataClass maxDataClass)
    {
        var text = new StringBuilder();
        var masked = false;
        foreach (var section in _sections)
        {
            if (text.Length > 0) text.Append('\n');
            switch (section.Kind)
            {
                case SectionKind.Text:
                    text.Append('[').Append(section.Title).Append("]\n").Append(section.Text).Append('\n');
                    break;

                case SectionKind.KeyValue:
                    text.Append('[').Append(section.Title).Append("]\n");
                    foreach (var column in ModelColumns(section))
                    {
                        var cell = section.Rows[0][column.Key];
                        if (cell.IsEmpty) continue;
                        var hide = column.DataClass > maxDataClass;
                        masked |= hide;
                        text.Append(column.Label).Append(": ").Append(hide ? Withheld : cell.ModelText).Append('\n');
                    }
                    break;

                case SectionKind.Table:
                    var columns = ModelColumns(section).ToList();
                    var rows = section.Rows.Take(_defaults.MaxModelRows).ToList();
                    text.Append('[').Append(section.Title).Append("] ")
                        .Append(section.TotalRows.ToString(CultureInfo.InvariantCulture))
                        .Append(section.TotalRows == 1 ? " row" : " rows");
                    if (rows.Count < section.TotalRows) text.Append(CultureInfo.InvariantCulture, $" (first {rows.Count} shown)");
                    text.Append('\n').AppendJoin(" | ", columns.Select(c => c.Label)).Append('\n');
                    foreach (var row in rows)
                    {
                        text.AppendJoin(" | ", columns.Select(c =>
                        {
                            var cell = row[c.Key];
                            if (cell.IsEmpty) return "-";
                            if (c.DataClass <= maxDataClass) return cell.ModelText.Replace('|', '/');
                            masked = true;
                            return Withheld;
                        })).Append('\n');
                    }
                    break;
            }
        }

        if (masked) text.Append("\nValues shown as ").Append(Withheld).Append(" are visible to the employee when this result is shown, but not to you.\n");
        return text.ToString().TrimEnd();
    }

    /// <summary>Plain-text rendering (no masking) for answers produced without an LLM.</summary>
    public string RenderPlainText() => RenderForModel(DataClass.Confidential);

    /// <summary>Every value in the result plus derived figures (row counts, column totals, date parts), for the grounding check.</summary>
    public IEnumerable<string> GroundingValues()
    {
        foreach (var section in _sections)
        {
            if (section.Text is { } text) yield return text;
            if (section.Kind == SectionKind.Table) yield return section.TotalRows.ToString(CultureInfo.InvariantCulture);

            foreach (var column in section.Columns)
            {
                decimal sum = 0;
                var numeric = 0;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var row in section.Rows)
                {
                    var cell = row[column.Key];
                    if (cell.IsEmpty) continue;
                    yield return cell.Canonical;
                    yield return cell.ModelText;
                    counts[cell.Canonical] = counts.GetValueOrDefault(cell.Canonical) + 1;

                    if (column.Format is FieldFormat.Number or FieldFormat.Bytes
                        && decimal.TryParse(cell.Canonical, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    {
                        sum += n;
                        numeric++;
                    }
                    if (column.Format is FieldFormat.Date or FieldFormat.DateTime
                        && DateTimeOffset.TryParse(cell.Canonical, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        // "Nov 12", "12th", "2 PM" all derive from the canonical date.
                        yield return d.Day.ToString(CultureInfo.InvariantCulture);
                        yield return d.ToString("%h", CultureInfo.InvariantCulture);
                    }
                }
                if (numeric > 1) yield return sum.ToString("0.############", CultureInfo.InvariantCulture);
                // "3 of your 5 holidays are in December": counts of a repeated value are derived, not invented.
                foreach (var count in counts.Values.Where(c => c > 1).Distinct()) yield return count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private IEnumerable<Column> ModelColumns(Section section) =>
        section.Columns.Where(c => c.View != FieldView.Block).Take(_defaults.MaxModelColumns);

    private List<AnswerBlock> BuildBlocks()
    {
        var blocks = new List<AnswerBlock>();
        foreach (var section in _sections.Where(s => !s.IsRawText))
        {
            var id = blocks.Count == 0 ? ResultId : $"{ResultId}.{blocks.Count + 1}";
            var columns = section.Columns.Where(c => c.View != FieldView.Model).ToList();
            blocks.Add(section.Kind switch
            {
                SectionKind.Table => new AnswerBlock(
                    id, AnswerBlockKind.Table, section.Title,
                    columns.Take(_defaults.MaxBlockColumns).Select(c => new AnswerColumn(c.Key, c.Label, c.Format)).ToList(),
                    section.Rows.Take(_defaults.MaxBlockRows)
                        .Select(r => (IReadOnlyList<string>)columns.Take(_defaults.MaxBlockColumns).Select(c => r[c.Key].Canonical).ToList())
                        .ToList(),
                    [], section.TotalRows, _source),
                SectionKind.KeyValue => new AnswerBlock(
                    id, AnswerBlockKind.KeyValue, section.Title, [], [],
                    columns.Select(c => (Column: c, Cell: section.Rows[0][c.Key]))
                        .Where(x => !x.Cell.IsEmpty)
                        .Select(x => new KeyValueItem(x.Column.Label, x.Cell.Canonical, x.Column.Format))
                        .ToList(),
                    1, _source),
                _ => new AnswerBlock(id, AnswerBlockKind.Text, section.Title, [], [], [], 0, _source, section.Text)
            });
        }
        return blocks;
    }
}
