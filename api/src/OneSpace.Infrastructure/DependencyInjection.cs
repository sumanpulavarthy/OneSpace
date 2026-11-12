using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
using OneSpace.Application.Policies;
using OneSpace.Infrastructure.Identity;
using OneSpace.Infrastructure.Jobs;
using OneSpace.Infrastructure.Persistence;
using OneSpace.Infrastructure.Security;

namespace OneSpace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceInfrastructure(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContextAccessor, HttpUserContextAccessor>();
        if (services.All(d => d.ServiceType != typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);

        services.AddOptions<MongoOptions>().Bind(configuration.GetSection(MongoOptions.SectionName));
        services.AddSingleton<MongoContext>();
        services.AddSingleton<IQueryLogRepository, QueryLogRepository>();
        services.AddSingleton<IPolicyDocumentRepository, PolicyDocumentRepository>();
        services.AddSingleton<IConversationStore, ConversationStore>();

        // FR-3 vector retrieval. Options are bound here and read by the embedder (OneSpace.Assistant) too.
        services.AddOptions<RetrievalOptions>()
            .Bind(configuration.GetSection(RetrievalOptions.SectionName))
            .Validate(o => o.VectorStore != VectorStoreKind.CosmosVCore || !o.Embeddings.Enabled || o.Embeddings.Dimensions is > 0,
                "Retrieval:Embeddings:Dimensions is required for the Cosmos vCore vector index.")
            .Validate(o => !o.Embeddings.Enabled || o.Embeddings.MaxDataClass >= Domain.Assistant.DataClass.Internal,
                "Retrieval:Embeddings:MaxDataClass must be at least Internal: policy text and search queries are Internal data.")
            .ValidateOnStart();
        services.AddSingleton<IPolicyChunkStore, PolicyChunkStore>();
        services.AddHostedService<MongoIndexInitializer>();

        // OAuth material (Zoho tokens, PKCE verifiers, client secret): encrypted at rest.
        services.AddOneSpaceDataProtection(configuration, isDevelopment);
        services.AddSingleton<OAuthSecretProtector>();
        services.AddSingleton<IZohoTokenStore, ZohoTokenStore>();
        services.AddSingleton<IZohoEmployeeDirectory, ZohoEmployeeDirectory>();
        services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
        services.AddSingleton<IOAuthClientStore, OAuthClientStore>();

        services.AddOptions<PolicySyncOptions>().Bind(configuration.GetSection(PolicySyncOptions.SectionName));
        services.AddHostedService<PolicySyncHostedService>();

        return services;
    }
}
