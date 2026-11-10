using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;
using OneSpace.Domain.Users;

namespace OneSpace.Application.Abstractions;

public interface IUserContextAccessor
{
    /// <summary>The authenticated employee. Throws when the request is anonymous.</summary>
    UserContext Current { get; }
}

public interface IQueryLogRepository
{
    Task AddAsync(QueryLogEntry entry, CancellationToken cancellationToken);

    /// <summary>The caller's own recent queries, newest first.</summary>
    Task<IReadOnlyList<QueryLogEntry>> GetRecentAsync(string userId, int limit, CancellationToken cancellationToken);
}

public interface IPolicyDocumentRepository
{
    Task UpsertManyAsync(IReadOnlyCollection<PolicyDocument> documents, CancellationToken cancellationToken);

    /// <summary>Removes cached documents that no longer exist in ComplyKaro.</summary>
    Task<long> DeleteMissingAsync(IReadOnlyCollection<string> keepIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicySearchResult>> SearchAsync(string text, int limit, CancellationToken cancellationToken);

    Task<PolicyDocument?> GetAsync(string id, CancellationToken cancellationToken);
}
