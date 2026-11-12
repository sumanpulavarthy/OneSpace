using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Policies;
using OneSpace.Assistant.Llm;
using OneSpace.Assistant.Telemetry;
using OpenAI;

namespace OneSpace.Assistant.Retrieval;

/// <summary>
/// <see cref="ITextEmbedder"/> over Microsoft.Extensions.AI's <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// OpenAI, Azure OpenAI (v1 endpoint) and Ollama all expose the OpenAI embeddings API, so one adapter covers them.
/// (Anthropic has no embeddings API; pair a Claude chat provider with any of these.)
/// </summary>
public sealed class TextEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator, string modelId, int? dimensions) : ITextEmbedder, IDisposable
{
    public bool IsEnabled => true;

    public string ModelId { get; } = modelId;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0) return [];
        var options = dimensions is { } d ? new EmbeddingGenerationOptions { Dimensions = d } : null;
        var embeddings = await generator.GenerateAsync(texts, options, cancellationToken);
        if (embeddings.Count != texts.Count)
            throw new InvalidOperationException($"Embedding model returned {embeddings.Count} vectors for {texts.Count} inputs.");
        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }

    public void Dispose() => generator.Dispose();

    public static ITextEmbedder Create(EmbeddingOptions options, ILoggerFactory loggerFactory)
    {
        if (!options.Enabled) return DisabledTextEmbedder.Instance;

        var endpoint = LlmProviderChain.OpenAiCompatibleEndpoint(new LlmProviderOptions
        {
            Type = options.Type switch
            {
                EmbeddingProviderType.AzureOpenAI => LlmProviderType.AzureOpenAI,
                EmbeddingProviderType.Ollama => LlmProviderType.Ollama,
                _ => LlmProviderType.OpenAI
            },
            Endpoint = options.Endpoint
        });
        if (options.Type == EmbeddingProviderType.AzureOpenAI && endpoint is null)
            throw new InvalidOperationException("Retrieval:Embeddings:Endpoint is required for Azure OpenAI.");
        if (options.Type is EmbeddingProviderType.OpenAI or EmbeddingProviderType.AzureOpenAI && string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("Retrieval:Embeddings:ApiKey is required (Retrieval--Embeddings--ApiKey in Key Vault).");

        var generator = new OpenAI.Embeddings.EmbeddingClient(
                options.Model,
                new ApiKeyCredential(string.IsNullOrWhiteSpace(options.ApiKey) ? "unused" : options.ApiKey),
                new OpenAIClientOptions { Endpoint = endpoint })
            .AsIEmbeddingGenerator(options.Dimensions)
            .AsBuilder()
            .UseOpenTelemetry(loggerFactory, AssistantTelemetry.LlmSourceName)
            .Build();
        return new TextEmbedder(generator, options.Model, options.Dimensions);
    }
}

/// <summary>No embedding model configured: retrieval is keyword-only.</summary>
public sealed class DisabledTextEmbedder : ITextEmbedder
{
    public static DisabledTextEmbedder Instance { get; } = new();

    public bool IsEnabled => false;

    public string ModelId => "none";

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No embedding model is configured (Retrieval:Embeddings).");
}
