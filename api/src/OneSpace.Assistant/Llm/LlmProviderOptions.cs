using OneSpace.Domain.Assistant;

namespace OneSpace.Assistant.Llm;

public enum LlmProviderType
{
    Anthropic,
    OpenAI,

    /// <summary>Azure OpenAI through its OpenAI-compatible v1 endpoint (<c>https://{resource}.openai.azure.com/openai/v1/</c>).</summary>
    AzureOpenAI,

    /// <summary>A local Ollama server through its OpenAI-compatible endpoint (default <c>http://localhost:11434/v1/</c>).</summary>
    Ollama
}

public sealed class LlmProviderOptions
{
    public LlmProviderType Type { get; set; }

    /// <summary>From Key Vault / user-secrets only. Not needed for Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Required for Azure OpenAI; optional override for the others.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Primary model (for Azure OpenAI: the deployment name).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Optional cheaper model for simple lookups (see Assistant:Routing).</summary>
    public string? FastModel { get; set; }

    /// <summary>Model ids in specs/llm/models.json when they differ from the deployment names (Azure OpenAI).</summary>
    public string? CatalogModel { get; set; }

    /// <inheritdoc cref="CatalogModel"/>
    public string? CatalogFastModel { get; set; }

    /// <summary>
    /// Most sensitive data this provider may receive (FR-7). Defaults to Internal so that a newly added provider sees
    /// no personal data until someone decides it may. Tools above it are not offered; fields above it are masked.
    /// </summary>
    public DataClass MaxDataClass { get; set; } = DataClass.Internal;
}
