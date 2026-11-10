using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;

namespace OneSpace.Infrastructure.Persistence;

public sealed class MongoOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Azure Cosmos DB for MongoDB (vCore) connection string — Key Vault reference in Azure,
    /// a local Mongo container in development. Never commit a real value.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string DatabaseName { get; set; } = "onespace";

    /// <summary>FR-4 logs are for debugging only; expire them so they don't become an accidental archive.</summary>
    public int QueryLogRetentionDays { get; set; } = 30;
}

/// <summary>The two Phase 0 collections (PRD Section 14). No live HR data is stored here.</summary>
public sealed class MongoContext
{
    public const string QueryLogsCollection = "queryLogs";
    public const string PolicyDocumentsCollection = "policyDocuments";
    public const string ZohoConnectionsCollection = "zohoConnections";
    public const string OAuthStatesCollection = "oauthStates";
    public const string OAuthClientsCollection = "oauthClients";

    static MongoContext() => ConfigureSerialization();

    public MongoContext(IOptions<MongoOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ConnectionString))
            throw new InvalidOperationException($"{MongoOptions.SectionName}:ConnectionString is not configured.");

        Options = o;
        var settings = MongoClientSettings.FromConnectionString(o.ConnectionString);
        settings.ApplicationName = "onespace-api";
        Database = new MongoClient(settings).GetDatabase(o.DatabaseName);
    }

    public MongoOptions Options { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<QueryLogEntry> QueryLogs => Database.GetCollection<QueryLogEntry>(QueryLogsCollection);

    public IMongoCollection<PolicyDocument> PolicyDocuments => Database.GetCollection<PolicyDocument>(PolicyDocumentsCollection);

    private static void ConfigureSerialization()
    {
        ConventionRegistry.Register("onespace", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        }, _ => true);

        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
    }
}
