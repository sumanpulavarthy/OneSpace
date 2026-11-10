using Microsoft.Extensions.Options;
using OneSpace.Infrastructure.Persistence;
using Testcontainers.MongoDb;

namespace OneSpace.IntegrationTests;

/// <summary>
/// A throwaway MongoDB container (wire-compatible with Azure Cosmos DB for MongoDB vCore for the features used).
/// Requires Docker.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder().WithImage("mongo:7.0").Build();

    public MongoContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = _container.GetConnectionString(),
            DatabaseName = $"onespace_test_{Guid.NewGuid():N}"
        }));
        await new MongoIndexInitializer(Context).StartAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
