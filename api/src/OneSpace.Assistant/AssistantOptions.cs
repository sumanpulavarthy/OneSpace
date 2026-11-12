using Microsoft.Extensions.AI;
using OneSpace.Assistant.Llm;
using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>"Llm" (default) or "RuleBased" — the keyword router for local demos without an LLM. "Claude" is accepted as "Llm".</summary>
    public string Engine { get; set; } = "Llm";

    /// <summary>LLM providers by name. Keys and endpoints come from Key Vault (<c>Assistant--Providers--{name}--ApiKey</c>).</summary>
    public Dictionary<string, LlmProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provider names in failover order. Required when more than one provider is configured.</summary>
    public List<string> Chain { get; set; } = [];

    /// <summary>Legacy single-provider settings: used as an Anthropic provider when <see cref="Providers"/> is empty.</summary>
    public string? ApiKey { get; set; }

    /// <inheritdoc cref="ApiKey"/>
    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxTokens { get; set; } = 4096;

    /// <summary>Chat lookups are simple; low effort keeps latency near the NFR target. Sent only to models with reasoning support.</summary>
    public ReasoningEffort Effort { get; set; } = ReasoningEffort.Low;

    public int MaxToolRounds { get; set; } = 5;

    /// <summary>Character budget for earlier turns sent with a follow-up question.</summary>
    public int MaxHistoryChars { get; set; } = 6000;

    /// <summary>Offer the model only the tools relevant to the question (plus those used in the previous turn).</summary>
    public bool ToolSelection { get; set; } = true;

    /// <summary>Refuse to start with a model that has not passed the eval gate (specs/llm/models.json). Off in Development.</summary>
    public bool RequireCertifiedModels { get; set; } = true;

    public RoutingOptions Routing { get; set; } = new();

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    public bool UsesLlm => !string.Equals(Engine, "RuleBased", StringComparison.OrdinalIgnoreCase);

    /// <summary>The configured providers, with the legacy single-provider settings mapped to an Anthropic provider.</summary>
    public IReadOnlyList<(string Name, LlmProviderOptions Options)> ResolveChain()
    {
        if (Providers.Count == 0)
        {
            return [("anthropic", new LlmProviderOptions
            {
                Type = LlmProviderType.Anthropic,
                ApiKey = ApiKey,
                Model = Model,
                MaxDataClass = DataClass.Personal
            })];
        }

        var names = Chain.Count > 0 ? Chain : Providers.Count == 1 ? [.. Providers.Keys] : null;
        if (names is null)
            throw new InvalidOperationException($"{SectionName}:Chain must list the provider order when more than one provider is configured.");

        return names.Select(name =>
            {
                if (!Providers.TryGetValue(name, out var options))
                    throw new InvalidOperationException($"{SectionName}:Chain names provider '{name}', which is not configured under {SectionName}:Providers.");
                // The existing Key Vault secret Assistant--ApiKey keeps working for an Anthropic provider without its own key.
                if (options.Type == LlmProviderType.Anthropic && string.IsNullOrWhiteSpace(options.ApiKey)) options.ApiKey = ApiKey;
                return (name, options);
            })
            .ToList();
    }
}

public sealed class RoutingOptions
{
    /// <summary>Send simple lookups to each provider's FastModel (when configured).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Redo a fast-model answer with the primary model when it fails the grounding check.</summary>
    public bool EscalateOnGroundingFailure { get; set; } = true;
}

public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 3;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(60);
}
