using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Policies;

namespace OneSpace.Application.Policies;

/// <summary>FR-3: refreshes the policy document cache from ComplyKaro, then the vector index built from it.</summary>
public sealed class SyncPoliciesUseCase(
    IComplyKaroSource source,
    IPolicyDocumentRepository repository,
    PolicyEmbeddingIndexer indexer,
    ILogger<SyncPoliciesUseCase> logger)
{
    private const int BatchSize = 100;

    public async Task<PolicySyncResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var seen = new List<string>();
        var batch = new List<PolicyDocument>(BatchSize);
        var syncedAt = DateTimeOffset.UtcNow;

        await foreach (var document in source.FetchPoliciesAsync(cancellationToken))
        {
            document.SyncedAt = syncedAt;
            batch.Add(document);
            seen.Add(document.Id);
            if (batch.Count == BatchSize)
            {
                await repository.UpsertManyAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0) await repository.UpsertManyAsync(batch, cancellationToken);

        // An empty pull is far more likely an outage than a real "all policies deleted"; keep the cache.
        var removed = seen.Count > 0 ? await repository.DeleteMissingAsync(seen, cancellationToken) : 0;
        logger.LogInformation("Policy sync complete: {Upserted} upserted, {Removed} removed", seen.Count, removed);

        var index = await TryIndexAsync(cancellationToken);
        return new PolicySyncResult(seen.Count, removed, syncedAt, index);
    }

    private async Task<PolicyIndexResult?> TryIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await indexer.IndexAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keyword search keeps working on the fresh cache; the index catches up on the next sync.
            logger.LogWarning(ex, "Policy vector indexing failed; search stays keyword-only for changed documents");
            return null;
        }
    }
}

/// <param name="Index">Vector indexing outcome; null when indexing failed.</param>
public sealed record PolicySyncResult(int Upserted, long Removed, DateTimeOffset SyncedAt, PolicyIndexResult? Index);
