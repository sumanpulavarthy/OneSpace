using Microsoft.Extensions.DependencyInjection;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Assistant;
using OneSpace.Application.Policies;

namespace OneSpace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceApplication(this IServiceCollection services)
    {
        services.AddScoped<AskAssistantUseCase>();
        services.AddScoped<SyncPoliciesUseCase>();
        services.AddScoped<PolicyEmbeddingIndexer>();
        services.AddScoped<IPolicySearch, HybridPolicySearch>();
        return services;
    }
}
