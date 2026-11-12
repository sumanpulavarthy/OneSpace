using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneSpace.Application.Abstractions;
using OneSpace.Application.Assistant;
using OneSpace.Application.Integrations;
using OneSpace.Assistant;
using OneSpace.Assistant.Engines;
using OneSpace.Assistant.Llm;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Users;
using Xunit.Abstractions;

namespace OneSpace.Evals;

/// <summary>Runs only when ONESPACE_EVALS=1: it calls a real LLM and costs money.</summary>
public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ONESPACE_EVALS") != "1")
            Skip = "Set ONESPACE_EVALS=1 and configure Assistant__Providers__* (or ANTHROPIC_API_KEY) to run the LLM eval gate.";
    }
}

/// <summary>
/// The eval gate (FR-7, ADR-0004). Runs the golden set end to end against the configured provider chain and scores
/// tool selection, numeric grounding, required facts and an LLM-as-judge clarity rating. Writes a report to
/// artifacts/evals; a model may be marked "certified" in specs/llm/models.json only with a passing report.
/// Configure exactly as the API: e.g. Assistant__Providers__oa__Type=OpenAI, __Model, __ApiKey, Assistant__Chain__0=oa.
/// </summary>
public sealed partial class GoldenSetEval(ITestOutputHelper output)
{
    private static readonly UserContext Employee = new("eval-user", "asha.rao@example.com", "Asha Rao", "asha.rao@example.com", null);

    [EvalFact]
    public async Task Golden_set_meets_certification_thresholds()
    {
        await using var services = BuildServices();
        var chain = services.GetRequiredService<LlmProviderChain>();
        var catalog = services.GetRequiredService<ModelCatalog>();
        var judge = chain.Providers[0].Primary.Client;
        var target = $"{chain.Providers[0].Options.Type}/{chain.Providers[0].Primary.ModelId}";

        var cases = new List<CaseResult>();
        foreach (var golden in Specs.GoldenSet.Cases)
        {
            await using var scope = services.CreateAsyncScope();
            var engine = scope.ServiceProvider.GetRequiredService<LlmAssistantEngine>();
            var stopwatch = Stopwatch.StartNew();
            var result = await engine.AnswerAsync(Employee, AssistantRequest.Single(golden.Question), null, CancellationToken.None);
            stopwatch.Stop();

            var shown = result.Answer + "\n" + string.Join("\n", result.Blocks.SelectMany(BlockText));
            var called = result.ToolCalls.Where(c => c.Outcome == ToolInvocationOutcome.Succeeded).Select(c => c.Name).ToHashSet();
            var caseResult = new CaseResult(
                golden.Id,
                golden.Question,
                result.Answer,
                [.. called],
                ToolsOk: golden.ExpectTools.All(called.Contains),
                Grounded: result.Grounding.Grounded,
                MentionsOk: golden.MustMention.All(m => shown.Contains(m, StringComparison.OrdinalIgnoreCase))
                    && (golden.MustNotMention ?? []).All(m => !shown.Contains(m, StringComparison.OrdinalIgnoreCase)),
                Judge: await JudgeAsync(judge, golden.Question, result),
                result.Grounding.UnverifiedValues,
                stopwatch.ElapsedMilliseconds,
                result.Usage?.Model);
            cases.Add(caseResult);
            output.WriteLine($"{(caseResult.Passed ? "PASS" : "FAIL")} {golden.Id}: tools={caseResult.ToolsOk} grounded={caseResult.Grounded} facts={caseResult.MentionsOk} judge={caseResult.Judge}");
        }

        var thresholds = catalog.CertificationThresholds;
        var scores = new Scores(
            ToolSelection: cases.Average(c => c.ToolsOk ? 1.0 : 0.0),
            Grounding: cases.Average(c => c.Grounded ? 1.0 : 0.0),
            MustMention: cases.Average(c => c.MentionsOk ? 1.0 : 0.0),
            Judge: cases.Average(c => c.Judge),
            P95LatencyMs: Percentile(cases.Select(c => c.LatencyMs).ToList(), 0.95));
        var passed = scores.ToolSelection >= thresholds.ToolSelection && scores.Grounding >= thresholds.Grounding
            && scores.MustMention >= thresholds.MustMention && scores.Judge >= thresholds.Judge;

        var report = WriteReport(target, scores, passed, cases);
        output.WriteLine($"{target}: toolSelection={scores.ToolSelection:P0} grounding={scores.Grounding:P0} facts={scores.MustMention:P0} judge={scores.Judge:0.0} p95={scores.P95LatencyMs}ms");
        output.WriteLine($"Report: {report}");
        Assert.True(passed, $"{target} did not meet the certification thresholds; see {report}.");
    }

    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Assistant:Engine"] = "Llm" })
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // isDevelopment: the gate itself must be able to run uncertified models.
        services.AddOneSpaceAssistant(configuration, isDevelopment: true);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedClock(Specs.Today)));
        services.AddSingleton<IZohoPeopleClient>(sp => new FixtureZoho(sp.GetRequiredService<IToolAllowlistGuard>()));
        services.AddSingleton<IMicrosoft365Client, FixtureMicrosoft365>();
        services.AddSingleton<FixturePolicies>();
        services.AddSingleton<IPolicyDocumentRepository>(sp => sp.GetRequiredService<FixturePolicies>());
        services.AddSingleton<IPolicySearch>(sp => sp.GetRequiredService<FixturePolicies>());
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>LLM-as-judge: clarity and helpfulness for an employee, 1–5. Correctness is scored separately (facts, grounding).</summary>
    private static async Task<double> JudgeAsync(IChatClient judge, string question, AssistantEngineResult result)
    {
        var shown = string.Join("\n", result.Blocks.Select(b => $"[{b.Kind} '{b.Title}', {b.TotalRows} rows shown to the employee]"));
        var prompt =
            $"""
            Rate how clear and helpful this HR assistant answer is for an employee, from 1 (useless) to 5 (excellent).
            Judge only clarity, directness and tone: is the direct answer first, short, in plain language, with the source named?
            Tables/cards listed under "Shown" are displayed to the employee below the text and count towards the answer.

            Question: {question}
            Answer: {result.Answer}
            Shown: {(shown.Length == 0 ? "nothing" : shown)}

            Reply with only the number.
            """;
        var response = await judge.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], new ChatOptions { MaxOutputTokens = 2000 });
        var match = Score().Match(response.Text);
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : 1;
    }

    private static IEnumerable<string> BlockText(AnswerBlock block) =>
        block.Rows.SelectMany(r => r).Concat(block.Items.Select(i => i.Value)).Append(block.Text ?? string.Empty);

    private static long Percentile(List<long> values, double p)
    {
        values.Sort();
        return values.Count == 0 ? 0 : values[(int)Math.Ceiling(p * values.Count) - 1];
    }

    private static string WriteReport(string target, Scores scores, bool passed, List<CaseResult> cases)
    {
        var directory = Path.Combine(Specs.Root, "artifacts", "evals");
        Directory.CreateDirectory(directory);
        var date = DateTimeOffset.UtcNow;
        var path = Path.Combine(directory, $"{SafeName().Replace(target, "_")}-{date:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            target,
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            goldenSetVersion = Specs.GoldenSet.Version,
            passed,
            scores,
            cases
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return path;
    }

    private sealed record Scores(double ToolSelection, double Grounding, double MustMention, double Judge, long P95LatencyMs);

    private sealed record CaseResult(
        string Id, string Question, string Answer, List<string> ToolsCalled,
        bool ToolsOk, bool Grounded, bool MentionsOk, double Judge,
        IReadOnlyList<string> UnverifiedValues, long LatencyMs, string? Model)
    {
        public bool Passed => ToolsOk && Grounded && MentionsOk && Judge >= 3;
    }

    [GeneratedRegex(@"[1-5](\.\d)?")]
    private static partial Regex Score();

    [GeneratedRegex(@"[^A-Za-z0-9.\-]")]
    private static partial Regex SafeName();
}
