namespace OneSpace.Domain.Policies;

/// <summary>
/// A ComplyKaro policy document cached for fast search (FR-3). ComplyKaro stays the source of truth;
/// this copy is refreshed by the scheduled sync and holds no employee-specific data.
/// </summary>
public sealed class PolicyDocument
{
    /// <summary>ComplyKaro's identifier; also the cache key.</summary>
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string Category { get; set; } = "General";
    public required string Content { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record PolicySearchResult(PolicyDocument Document, double Score, string Snippet);
