using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Integrations;
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
        services.AddHostedService<MongoIndexInitializer>();

        // OAuth material (Zoho tokens, PKCE verifiers, client secret): encrypted at rest.
        services.AddOneSpaceDataProtection(configuration, isDevelopment);
        services.AddSingleton<OAuthSecretProtector>();
        services.AddSingleton<IZohoTokenStore, ZohoTokenStore>();
        services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
        services.AddSingleton<IOAuthClientStore, OAuthClientStore>();

        services.AddOptions<PolicySyncOptions>().Bind(configuration.GetSection(PolicySyncOptions.SectionName));
        services.AddHostedService<PolicySyncHostedService>();

        return services;
    }
}
