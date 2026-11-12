using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OneSpace.Api.Assistant;
using OneSpace.Assistant;
using OneSpace.Assistant.Guard;
using OneSpace.Assistant.Llm;
using OneSpace.Assistant.Presentation;
using OneSpace.Assistant.Tools;
using OneSpace.Domain;
using OneSpace.Domain.Assistant;

namespace OneSpace.ContractTests;

/// <summary>
/// FR-6/FR-7 contracts: the presentation spec and model catalog compiled into the API are the reviewed files,
/// every tool has a reviewed data class, the production config only uses models that passed the eval gate,
/// and the streaming endpoint speaks the same shape as the GraphQL schema.
/// </summary>
public sealed partial class AssistantSpecsContractTests
{
    [Theory]
    [InlineData("OneSpace.Assistant.presentation.json", "mcp/presentation.json")]
    [InlineData("OneSpace.Assistant.models.json", "llm/models.json")]
    public void Embedded_spec_matches_spec_file(string resource, string specFile)
    {
        using var stream = typeof(PresentationSpec).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var embedded = reader.ReadToEnd();
        var file = File.ReadAllText(Path.Combine(SpecPaths.Root, "specs", specFile));

        Assert.Equal(SpecPaths.Normalize(file), SpecPaths.Normalize(embedded));
    }

    [Fact]
    public void Every_tool_has_a_reviewed_presentation_and_data_class()
    {
        var spec = PresentationSpec.LoadEmbedded();
        var tools = AssistantToolbox.BuiltInTools
            .Concat(ToolAllowlistGuard.FromEmbeddedSpec().AllowedTools.Select(t => AssistantToolbox.ZohoPrefix + t));

        Assert.All(tools, tool => Assert.NotNull(spec.ForTool(tool)));
    }

    [Fact]
    public void Production_configuration_passes_the_eval_gate()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(SpecPaths.Root, "api", "src", "OneSpace.Api", "appsettings.json"))
            .Build();
        var options = configuration.GetSection(AssistantOptions.SectionName).Get<AssistantOptions>()!;
        foreach (var provider in options.Providers.Values) provider.ApiKey ??= "contract-test";

        using var chain = LlmProviderChain.Create(options, ModelCatalog.LoadEmbedded(), requireCertified: true,
            NullLoggerFactory.Instance, TimeProvider.System);

        Assert.All(chain.Providers, p => Assert.True(p.Options.MaxDataClass <= DataClass.Personal,
            $"Provider '{p.Name}' may receive {p.Options.MaxDataClass} data; confidential text must stay masked for cloud LLMs."));
    }

    [Fact]
    public void Model_catalog_is_well_formed()
    {
        var catalog = ModelCatalog.LoadEmbedded();
        Assert.NotEmpty(catalog.Models);
        Assert.All(catalog.Models, m => Assert.Contains(m.Certification.Status, new[] { "certified", "baseline", "pending", "failed" }));
        Assert.Equal(catalog.Models.Count, catalog.Models.Select(m => (m.Provider, m.Model.ToLowerInvariant())).Distinct().Count());
    }

    [Theory]
    [InlineData(typeof(SourceSystem), "SourceSystem")]
    [InlineData(typeof(ToolInvocationOutcome), "ToolInvocationOutcome")]
    [InlineData(typeof(AnswerBlockKind), "AnswerBlockKind")]
    [InlineData(typeof(FieldFormat), "FieldFormat")]
    public void Streamed_enum_values_match_the_graphql_schema(Type enumType, string graphQLName)
    {
        var policy = new GraphQLEnumNamingPolicy();
        var streamed = Enum.GetNames(enumType).Select(policy.ConvertName);

        Assert.Equal(SchemaBlock("enum", graphQLName), streamed);
    }

    [Fact]
    public void Streamed_answer_has_the_graphql_answer_fields()
    {
        var answer = new AssistantAnswer("s", "a", "i", [], [], 1, [], GroundingReport.NotChecked);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, AssistantStreamEndpoint.JsonOptions));

        Assert.Equal(SchemaBlock("type", "AssistantAnswer").Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    /// <summary>Member names of a type or enum in specs/graphql/schema.graphql.</summary>
    private static List<string> SchemaBlock(string keyword, string name)
    {
        var schema = File.ReadAllText(SpecPaths.GraphQLSchema);
        var body = Regex.Match(schema, $@"{keyword} {name} \{{(?<body>[^}}]*)\}}").Groups["body"].Value;
        return body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Select(l => MemberName().Match(l).Value)
            .ToList();
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]+")]
    private static partial Regex MemberName();
}
