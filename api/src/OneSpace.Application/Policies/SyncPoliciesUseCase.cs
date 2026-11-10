using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Domain.Policies;

namespace OneSpace.Application.Policies;

/// <summary>FR-3: refreshes the policy document cache from ComplyKaro.</summary>
public sealed class SyncPoliciesUseCase(
    IComplyKaroSource source,
    IPolicyDocumentRepository repository,
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
        return new PolicySyncResult(seen.Count, removed, syncedAt);
    }
}

public sealed record PolicySyncResult(int Upserted, long Removed, DateTimeOffset SyncedAt);
