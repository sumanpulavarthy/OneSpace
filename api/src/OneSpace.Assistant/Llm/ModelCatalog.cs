using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneSpace.Assistant.Llm;

/// <summary>specs/llm/models.json: the model capability matrix and eval-gate status (FR-7), embedded at build time.</summary>
public sealed class ModelCatalog
{
    private const string ResourceName = "OneSpace.Assistant.models.json";

    public string Version { get; init; } = "1";
    public CertificationThresholds CertificationThresholds { get; init; } = new();
    public List<ModelCapabilities> Models { get; init; } = [];

    public ModelCapabilities? Find(LlmProviderType provider, string model) =>
        Models.FirstOrDefault(m => m.Provider == provider && string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));

    public static ModelCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded model catalog '{ResourceName}' is missing.");
        return Parse(stream);
    }

    public static ModelCatalog Parse(Stream json) =>
        JsonSerializer.Deserialize<ModelCatalog>(json, JsonOptions) ?? throw new InvalidOperationException("Model catalog is empty.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class CertificationThresholds
{
    /// <summary>Share of cases where every expected tool was called.</summary>
    public double ToolSelection { get; init; } = 0.9;

    /// <summary>Share of answers that passed the numeric grounding check.</summary>
    public double Grounding { get; init; } = 0.95;

    /// <summary>Share of cases whose answer (or shown blocks) contained every required fact.</summary>
    public double MustMention { get; init; } = 0.9;

    /// <summary>Mean LLM-as-judge clarity score, 1–5.</summary>
    public double Judge { get; init; } = 4.0;
}

public sealed class ModelCapabilities
{
    public LlmProviderType Provider { get; init; }
    public string Model { get; init; } = string.Empty;
    public bool ToolCalling { get; init; }
    public bool ParallelToolCalls { get; init; }

    /// <summary>Accepts a reasoning-effort setting; sending one to a model without it is an API error.</summary>
    public bool Reasoning { get; init; }

    public bool PromptCaching { get; init; }
    public Certification Certification { get; init; } = new();

    /// <summary>Passed the eval gate, or is the pre-gate production baseline.</summary>
    public bool IsApproved => Certification.Status is "certified" or "baseline";
}

public sealed class Certification
{
    /// <summary>"certified", "baseline", "pending" or "failed".</summary>
    public string Status { get; init; } = "pending";
    public string? Date { get; init; }
    public string? Report { get; init; }
    public string? Notes { get; init; }
}
