using MongoDB.Driver;
using OneSpace.Application.Integrations;

namespace OneSpace.Infrastructure.Persistence;

/// <summary>Employee email → Zoho erecno, keyed by lower-cased email.</summary>
public sealed class ZohoEmployeeDirectory(MongoContext context, TimeProvider clock) : IZohoEmployeeDirectory
{
    private IMongoCollection<EmployeeDocument> Employees =>
        context.Database.GetCollection<EmployeeDocument>(MongoContext.ZohoEmployeesCollection);

    public async Task<long?> FindErecnoAsync(string email, CancellationToken cancellationToken)
    {
        var key = Key(email);
        var doc = await Employees.Find(d => d.Id == key).FirstOrDefaultAsync(cancellationToken);
        return doc?.Erecno;
    }

    public Task SetErecnoAsync(string email, long erecno, CancellationToken cancellationToken)
    {
        var key = Key(email);
        return Employees.ReplaceOneAsync(d => d.Id == key,
            new EmployeeDocument { Id = key, Erecno = erecno, UpdatedAt = clock.GetUtcNow() },
            new ReplaceOptions { IsUpsert = true }, cancellationToken);
    }

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    internal sealed class EmployeeDocument
    {
        public required string Id { get; init; }
        public long Erecno { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
