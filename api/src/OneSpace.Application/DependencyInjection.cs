using Microsoft.Extensions.DependencyInjection;
using OneSpace.Application.Assistant;
using OneSpace.Application.Policies;

namespace OneSpace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceApplication(this IServiceCollection services)
    {
        services.AddScoped<AskAssistantUseCase>();
        services.AddScoped<SyncPoliciesUseCase>();
        return services;
    }
}
