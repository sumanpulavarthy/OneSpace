using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneSpace.Application.Assistant;
using OneSpace.Application.Integrations;
using OneSpace.Assistant.Engines;
using OneSpace.Assistant.Guard;
using OneSpace.Assistant.Llm;
using OneSpace.Application.Policies;
using OneSpace.Assistant.Presentation;
using OneSpace.Assistant.Retrieval;
using OneSpace.Assistant.Tools;

namespace OneSpace.Assistant;

public static class DependencyInjection
{
    public static IServiceCollection AddOneSpaceAssistant(this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        services.AddOptions<AssistantOptions>().Bind(configuration.GetSection(AssistantOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        // Built once from the embedded specs; an invalid spec fails startup rather than a request.
        services.AddSingleton<IToolAllowlistGuard>(_ => ToolAllowlistGuard.FromEmbeddedSpec());
        services.AddSingleton(_ => PresentationSpec.LoadEmbedded());
        services.AddSingleton(_ => ModelCatalog.LoadEmbedded());
        services.AddSingleton<ToolResultPresenter>();
        // FR-3 embeddings (Retrieval:Embeddings; options bound by Infrastructure). Disabled = keyword-only search.
        services.AddSingleton(sp => TextEmbedder.Create(sp.GetRequiredService<IOptions<RetrievalOptions>>().Value.Embeddings,
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddScoped<AssistantToolbox>();
        services.AddScoped<IAssistantToolbox>(sp => sp.GetRequiredService<AssistantToolbox>());

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
            // The eval gate applies outside Development unless explicitly turned off in configuration.
            var requireCertified = options.RequireCertifiedModels && !isDevelopment;
            return LlmProviderChain.Create(options, sp.GetRequiredService<ModelCatalog>(), requireCertified,
                sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<ProviderChainChatClient>();
        services.AddScoped<LlmAssistantEngine>();
        services.AddScoped<RuleBasedAssistantEngine>();
        services.AddScoped<IAssistantEngine>(sp => sp.GetRequiredService<IOptions<AssistantOptions>>().Value.UsesLlm
            ? sp.GetRequiredService<LlmAssistantEngine>()
            : sp.GetRequiredService<RuleBasedAssistantEngine>());

        // Build the provider chain at startup so a bad provider config or an uncertified model fails the deploy, not a request.
        services.AddHostedService<LlmChainValidator>();
        return services;
    }

    private sealed class LlmChainValidator(IServiceProvider services, IOptions<AssistantOptions> options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (options.Value.UsesLlm) services.GetRequiredService<LlmProviderChain>();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
