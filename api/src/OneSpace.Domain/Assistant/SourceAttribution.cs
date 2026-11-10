namespace OneSpace.Domain.Assistant;

/// <summary>Where part of an answer came from, e.g. "From Zoho People" or a ComplyKaro policy title.</summary>
public sealed record SourceAttribution(SourceSystem System, string Label, string? Reference = null);
