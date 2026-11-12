using OneSpace.Assistant.Guard;
using OneSpace.Assistant.Tools;

namespace OneSpace.Evals;

/// <summary>Always runs (no LLM): the golden set and fixtures stay consistent with the tools that exist.</summary>
public sealed class GoldenSetSpecTests
{
    [Fact]
    public void Every_expected_tool_exists_and_has_fixture_data()
    {
        var zohoFixtures = Specs.Fixtures["zoho"]!["results"]!.AsObject().Select(p => AssistantToolbox.ZohoPrefix + p.Key).ToHashSet();
        var allowed = ToolAllowlistGuard.FromEmbeddedSpec().AllowedTools.Select(t => AssistantToolbox.ZohoPrefix + t).ToHashSet();

        foreach (var tool in Specs.GoldenSet.Cases.SelectMany(c => c.ExpectTools))
        {
            if (tool.StartsWith(AssistantToolbox.ZohoPrefix, StringComparison.Ordinal))
            {
                Assert.Contains(tool, allowed);
                Assert.Contains(tool, zohoFixtures);
            }
            else
            {
                Assert.Contains(tool, AssistantToolbox.BuiltInTools);
            }
        }
    }

    [Fact]
    public void Case_ids_are_unique() =>
        Assert.Equal(Specs.GoldenSet.Cases.Count, Specs.GoldenSet.Cases.Select(c => c.Id).Distinct().Count());

    [Fact]
    public async Task Fixture_clients_serve_every_source()
    {
        var m365 = new FixtureMicrosoft365();
        Assert.Equal("Asha Rao", (await m365.GetMyProfileAsync(CancellationToken.None)).DisplayName);
        Assert.NotEmpty(await m365.GetMyTasksAsync(10, CancellationToken.None));
        Assert.NotEmpty(await new FixturePolicies().SearchAsync("work from home", 3, CancellationToken.None));

        var zoho = new FixtureZoho(ToolAllowlistGuard.FromEmbeddedSpec());
        Assert.Equal(6, (await zoho.ListAllowedToolsAsync(CancellationToken.None)).Count);
        Assert.Contains("Casual Leave", (await zoho.CallToolAsync("getLeaveBalance", null, CancellationToken.None)).Text, StringComparison.Ordinal);
    }
}
