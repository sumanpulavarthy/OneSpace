using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OneSpace.Assistant.Telemetry;
using OneSpace.Domain.Assistant;
using OpenAI;

namespace OneSpace.Assistant.Llm;

public enum ModelTier
{
    Primary,
    Fast
}

/// <param name="Capabilities">From specs/llm/models.json; null only for an unlisted model in Development.</param>
public sealed record LlmModel(string ModelId, ModelCapabilities? Capabilities, IChatClient Client)
{
    public bool SupportsReasoning(LlmProviderType provider) => Capabilities?.Reasoning ?? provider == LlmProviderType.Anthropic;
}

public sealed record LlmProvider(string Name, LlmProviderOptions Options, LlmModel Primary, LlmModel? Fast, CircuitBreaker Breaker)
{
    public LlmModel For(ModelTier tier) => tier == ModelTier.Fast && Fast is not null ? Fast : Primary;
}

/// <summary>The configured LLM providers in failover order, each with its primary and optional fast model.</summary>
public sealed class LlmProviderChain(IReadOnlyList<LlmProvider> providers) : IDisposable
{
    public IReadOnlyList<LlmProvider> Providers { get; } = providers.Count > 0
        ? providers
        : throw new InvalidOperationException("At least one LLM provider is required.");

    /// <summary>
    /// The most sensitive data the assistant may send. Any provider in the chain may end up answering (failover),
    /// so the chain is only as trusted as its least trusted member.
    /// </summary>
    public DataClass MaxDataClass => Providers.Min(p => p.Options.MaxDataClass);

    public bool HasFastTier => Providers.Any(p => p.Fast is not null);

    public void Dispose()
    {
        foreach (var provider in Providers)
        {
            provider.Primary.Client.Dispose();
            provider.Fast?.Client.Dispose();
        }
    }

    /// <summary>Builds and validates the chain. Every problem is reported at once so a bad config fails startup clearly.</summary>
    public static LlmProviderChain Create(AssistantOptions options, ModelCatalog catalog, bool requireCertified, ILoggerFactory loggerFactory, TimeProvider clock)
    {
        var errors = new List<string>();
        var providers = new List<LlmProvider>();

        foreach (var (name, provider) in options.ResolveChain())
        {
            if (string.IsNullOrWhiteSpace(provider.Model)) errors.Add($"Provider '{name}': Model is required.");
            if (provider.Type == LlmProviderType.AzureOpenAI && string.IsNullOrWhiteSpace(provider.Endpoint))
                errors.Add($"Provider '{name}': Endpoint is required for Azure OpenAI.");
            if (provider.Type is LlmProviderType.OpenAI or LlmProviderType.AzureOpenAI && string.IsNullOrWhiteSpace(provider.ApiKey))
                errors.Add($"Provider '{name}': ApiKey is required (Assistant--Providers--{name}--ApiKey in Key Vault).");

            var primary = Capabilities(name, provider, provider.CatalogModel ?? provider.Model, catalog, requireCertified, errors);
            var fast = string.IsNullOrWhiteSpace(provider.FastModel)
                ? null
                : Capabilities(name, provider, provider.CatalogFastModel ?? provider.FastModel, catalog, requireCertified, errors);
            if (errors.Count > 0) continue;

            providers.Add(new LlmProvider(
                name,
                provider,
                new LlmModel(provider.Model, primary, BuildClient(provider, provider.Model, loggerFactory)),
                fast is null && string.IsNullOrWhiteSpace(provider.FastModel)
                    ? null
                    : new LlmModel(provider.FastModel!, fast, BuildClient(provider, provider.FastModel!, loggerFactory)),
                new CircuitBreaker(options.CircuitBreaker.FailureThreshold, options.CircuitBreaker.BreakDuration, clock)));
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Assistant LLM configuration is invalid:\n- " + string.Join("\n- ", errors));
        return new LlmProviderChain(providers);
    }

    private static ModelCapabilities? Capabilities(string name, LlmProviderOptions provider, string model, ModelCatalog catalog, bool requireCertified, List<string> errors)
    {
        var capabilities = catalog.Find(provider.Type, model);
        if (capabilities is null)
        {
            if (requireCertified) errors.Add($"Provider '{name}': {provider.Type} model '{model}' is not in specs/llm/models.json.");
            return null;
        }
        if (!capabilities.ToolCalling)
            errors.Add($"Provider '{name}': model '{model}' does not support tool calling.");
        else if (requireCertified && !capabilities.IsApproved)
            errors.Add($"Provider '{name}': model '{model}' has not passed the eval gate (certification: {capabilities.Certification.Status}). Run api/tests/OneSpace.Evals.");
        return capabilities;
    }

    internal static IChatClient BuildClient(LlmProviderOptions provider, string model, ILoggerFactory loggerFactory)
    {
        var inner = provider.Type switch
        {
            // Without an explicit key the SDK resolves ANTHROPIC_API_KEY (or another configured credential).
            LlmProviderType.Anthropic => (string.IsNullOrWhiteSpace(provider.ApiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = provider.ApiKey })
                .AsIChatClient(model),
            _ => new OpenAI.Chat.ChatClient(
                    model,
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(provider.ApiKey) ? "unused" : provider.ApiKey),
                    new OpenAIClientOptions { Endpoint = OpenAiCompatibleEndpoint(provider) })
                .AsIChatClient()
        };

        // GenAI semantic-convention traces and metrics (model, tokens, latency). Message contents are not recorded.
        return inner.AsBuilder().UseOpenTelemetry(loggerFactory, AssistantTelemetry.LlmSourceName).Build();
    }

    internal static Uri? OpenAiCompatibleEndpoint(LlmProviderOptions provider)
    {
        var configured = string.IsNullOrWhiteSpace(provider.Endpoint) ? null : new Uri(provider.Endpoint.TrimEnd('/') + "/");
        return provider.Type switch
        {
            // A bare resource URL gets the v1 path; a full path is used as given.
            LlmProviderType.AzureOpenAI when configured is not null && configured.AbsolutePath == "/" => new Uri(configured, "openai/v1/"),
            LlmProviderType.Ollama => configured ?? new Uri("http://localhost:11434/v1/"),
            _ => configured
        };
    }
}
