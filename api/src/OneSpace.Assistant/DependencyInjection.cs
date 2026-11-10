using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OneSpace.Application.Assistant;
using OneSpace.Application.Integrations;
using OneSpace.Assistant.Engines;
using OneSpace.Assistant.Guard;
using OneSpace.Assistant.Tools;

namespace OneSpace.Assistant;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>().Bind(configuration.GetSection(AssistantOptions.SectionName));
        services.AddSingleton(TimeProvider.System);

        // Built once from the embedded spec; an invalid spec fails startup rather than a request.
        services.AddSingleton<IToolAllowlistGuard>(_ => ToolAllowlistGuard.FromEmbeddedSpec());
        services.AddScoped<AssistantToolbox>();

        services.AddSingleton(sp =>
        {
            var apiKey = sp.GetRequiredService<IOptions<AssistantOptions>>().Value.ApiKey;
            // Without an explicit key the SDK resolves ANTHROPIC_API_KEY (or another configured credential).
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        });
        services.AddScoped<ClaudeAssistantEngine>();
        services.AddScoped<RuleBasedAssistantEngine>();
        services.AddScoped<IAssistantEngine>(sp =>
        {
            var engine = sp.GetRequiredService<IOptions<AssistantOptions>>().Value.Engine;
            return string.Equals(engine, "RuleBased", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<RuleBasedAssistantEngine>()
                : sp.GetRequiredService<ClaudeAssistantEngine>();
        });

        return services;
    }
}
