using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OneSpace.Assistant;
using OneSpace.Assistant.Llm;
using OneSpace.Assistant.Tools;
using OneSpace.Domain.Assistant;

namespace OneSpace.UnitTests.Llm;

public class ProviderChainTests
{
    private static readonly ModelCatalog Catalog = ModelCatalog.LoadEmbedded();

    internal static LlmProvider Provider(string name, IChatClient primary, IChatClient? fast = null,
        DataClass maxDataClass = DataClass.Personal, ModelCapabilities? capabilities = null) =>
        new(name, new LlmProviderOptions { Type = LlmProviderType.OpenAI, Model = "primary", MaxDataClass = maxDataClass },
            new LlmModel("primary", capabilities, primary),
            fast is null ? null : new LlmModel("fast", capabilities, fast),
            new CircuitBreaker(2, TimeSpan.FromMinutes(1), TimeProvider.System));

    [Fact]
    public async Task Fails_over_to_the_next_provider_and_records_it()
    {
        var first = new ScriptedChatClient().Fail(new HttpRequestException("503"));
        var second = new ScriptedChatClient().Reply("hello");
        var client = new ProviderChainChatClient(new LlmProviderChain([Provider("a", first), Provider("b", second)]), NullLogger<ProviderChainChatClient>.Instance);
        var info = new LlmCallInfo();

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { AdditionalProperties = new() { [ProviderChainChatClient.CallInfoKey] = info } });

        Assert.Equal("hello", response.Text);
        Assert.Equal("b", info.Provider);
        Assert.True(info.FailedOver);
    }

    [Fact]
    public async Task Open_breaker_skips_a_failing_provider()
    {
        var first = new ScriptedChatClient().Fail(new HttpRequestException()).Fail(new HttpRequestException());
        var second = new ScriptedChatClient().Reply("1").Reply("2").Reply("3");
        var client = new ProviderChainChatClient(new LlmProviderChain([Provider("a", first), Provider("b", second)]), NullLogger<ProviderChainChatClient>.Instance);

        for (var i = 0; i < 3; i++) await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(2, first.Calls.Count); // threshold 2: the third request goes straight to "b"
        Assert.Equal(3, second.Calls.Count);
    }

    [Fact]
    public async Task All_providers_failing_raises_unavailable()
    {
        var client = new ProviderChainChatClient(
            new LlmProviderChain([Provider("a", new ScriptedChatClient().Fail(new HttpRequestException()))]), NullLogger<ProviderChainChatClient>.Instance);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task Fast_tier_uses_the_fast_model_and_bookkeeping_keys_are_not_forwarded()
    {
        var primary = new ScriptedChatClient();
        var fast = new ScriptedChatClient().Reply("quick");
        var client = new ProviderChainChatClient(new LlmProviderChain([Provider("a", primary, fast)]), NullLogger<ProviderChainChatClient>.Instance);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
            AdditionalProperties = new() { [ProviderChainChatClient.TierKey] = ModelTier.Fast, ["keep"] = 1 }
        });

        var sent = Assert.Single(fast.Calls).Options!;
        Assert.Equal("fast", sent.ModelId);
        Assert.Null(sent.Reasoning); // OpenAI model without reasoning capability
        Assert.False(sent.AdditionalProperties!.ContainsKey(ProviderChainChatClient.TierKey));
        Assert.True(sent.AdditionalProperties.ContainsKey("keep"));
    }

    [Fact]
    public void Chain_is_only_as_trusted_as_its_least_trusted_provider()
    {
        var chain = new LlmProviderChain([
            Provider("a", new ScriptedChatClient(), maxDataClass: DataClass.Confidential),
            Provider("b", new ScriptedChatClient(), maxDataClass: DataClass.Internal)]);
        Assert.Equal(DataClass.Internal, chain.MaxDataClass);
    }

    [Fact]
    public void Uncertified_model_is_rejected_when_the_gate_is_on()
    {
        var options = new AssistantOptions
        {
            Providers = { ["oa"] = new LlmProviderOptions { Type = LlmProviderType.OpenAI, ApiKey = "k", Model = "gpt-4.1" } }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmProviderChain.Create(options, Catalog, requireCertified: true, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Contains("eval gate", ex.Message, StringComparison.Ordinal);

        using var chain = LlmProviderChain.Create(options, Catalog, requireCertified: false, NullLoggerFactory.Instance, TimeProvider.System);
        Assert.Equal("oa", Assert.Single(chain.Providers).Name);
    }

    [Fact]
    public void Configuration_errors_are_reported_together()
    {
        var options = new AssistantOptions
        {
            Providers = { ["az"] = new LlmProviderOptions { Type = LlmProviderType.AzureOpenAI, Model = "gpt-4.1" } }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmProviderChain.Create(options, Catalog, requireCertified: false, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Contains("Endpoint is required", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ApiKey is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chain_naming_an_unconfigured_provider_is_rejected()
    {
        var options = new AssistantOptions
        {
            Chain = ["a", "missing"],
            Providers = { ["a"] = new LlmProviderOptions { Type = LlmProviderType.Ollama, Model = "qwen2.5:14b" } }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LlmProviderChain.Create(options, Catalog, requireCertified: false, NullLoggerFactory.Instance, TimeProvider.System));
        Assert.Contains("'missing'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_settings_become_an_anthropic_provider_with_the_baseline_model()
    {
        var options = new AssistantOptions { ApiKey = "sk-test" };

        using var chain = LlmProviderChain.Create(options, Catalog, requireCertified: true, NullLoggerFactory.Instance, TimeProvider.System);

        var provider = Assert.Single(chain.Providers);
        Assert.Equal(LlmProviderType.Anthropic, provider.Options.Type);
        Assert.Equal("claude-opus-5-5", provider.Primary.ModelId);
        Assert.Equal(DataClass.Personal, chain.MaxDataClass);
    }

    [Theory]
    [InlineData(LlmProviderType.AzureOpenAI, "https://res.openai.azure.com", "https://res.openai.azure.com/openai/v1/")]
    [InlineData(LlmProviderType.AzureOpenAI, "https://res.openai.azure.com/openai/v1", "https://res.openai.azure.com/openai/v1/")]
    [InlineData(LlmProviderType.Ollama, null, "http://localhost:11434/v1/")]
    public void Openai_compatible_endpoints_are_normalised(LlmProviderType type, string? configured, string expected) =>
        Assert.Equal(expected, LlmProviderChain.OpenAiCompatibleEndpoint(new LlmProviderOptions { Type = type, Endpoint = configured })!.ToString());

    [Theory]
    [InlineData("What's my leave balance?", ModelTier.Fast)]
    [InlineData("Compare my leave balance with the carry forward policy", ModelTier.Primary)]
    [InlineData("Am I on leave during my meetings next week?", ModelTier.Primary)]
    public void Router_sends_simple_lookups_to_the_fast_model(string question, ModelTier tier) =>
        Assert.Equal(tier, ModelRouter.Choose(question, ToolSelector.DomainsOf(question), hasFastTier: true));

    [Fact]
    public void Router_uses_primary_when_no_fast_model_is_configured() =>
        Assert.Equal(ModelTier.Primary, ModelRouter.Choose("leave balance", new HashSet<string> { "leave" }, hasFastTier: false));
}
