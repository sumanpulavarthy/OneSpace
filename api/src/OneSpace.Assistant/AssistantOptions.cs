using Anthropic.Models.Beta.Messages;

namespace OneSpace.Assistant;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>"Claude" (default) or "RuleBased" — the keyword router used for local demos without an API key.</summary>
    public string Engine { get; set; } = "Claude";

    /// <summary>Anthropic API key. Supply from Key Vault / user-secrets, never appsettings.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxTokens { get; set; } = 4096;

    /// <summary>Chat lookups are simple; low effort keeps latency near the NFR target. Raise if answers are weak.</summary>
    public Effort Effort { get; set; } = Effort.Low;

    public int MaxToolRounds { get; set; } = 5;
}
