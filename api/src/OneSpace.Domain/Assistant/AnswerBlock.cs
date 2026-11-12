namespace OneSpace.Domain.Assistant;

public enum AnswerBlockKind
{
    Text,
    Table,
    KeyValue
}

/// <summary>How a value should be formatted for display. Values are sent in a canonical, culture-invariant form.</summary>
public enum FieldFormat
{
    Text,
    Number,

    /// <summary>Canonical form <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>Canonical form ISO 8601 with offset.</summary>
    DateTime,
    Boolean,
    Link,
    Bytes
}

/// <summary>
/// A structured part of an answer (FR-6). Built by the server from a tool's own output, never written by the
/// LLM, so the figures an employee sees in a table or card are exactly what the source system returned.
/// </summary>
/// <param name="Id">The tool result id the LLM refers to (e.g. <c>t1</c>, or <c>t1.2</c> for a result's second block).</param>
/// <param name="Rows">Table rows, one canonical string per column (empty string for no value).</param>
/// <param name="TotalRows">Rows the source returned; more than <see cref="Rows"/> when the table was capped.</param>
public sealed record AnswerBlock(
    string Id,
    AnswerBlockKind Kind,
    string? Title,
    IReadOnlyList<AnswerColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<KeyValueItem> Items,
    int TotalRows,
    SourceAttribution? Source,
    string? Text = null);

public sealed record AnswerColumn(string Key, string Label, FieldFormat Format);

public sealed record KeyValueItem(string Label, string Value, FieldFormat Format);
