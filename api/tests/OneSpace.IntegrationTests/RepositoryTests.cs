using OneSpace.Domain;
using OneSpace.Domain.Assistant;
using OneSpace.Domain.Policies;
using OneSpace.Infrastructure.Persistence;

namespace OneSpace.IntegrationTests;

public sealed class RepositoryTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task Policy_search_ranks_title_matches_and_returns_snippet()
    {
        var repo = new PolicyDocumentRepository(mongo.Context);
        await repo.UpsertManyAsync(
        [
            Policy("wfh", "Work From Home Policy", "Employees may work from home two days per week.\n\nBe reachable on Teams."),
            Policy("travel", "Travel Policy", "Claims within 30 days. Home office equipment is not covered.")
        ], CancellationToken.None);

        var results = await repo.SearchAsync("work from home", 5, CancellationToken.None);

        Assert.Equal("wfh", results[0].Document.Id);
        Assert.Contains("two days per week", results[0].Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upsert_replaces_and_delete_missing_prunes()
    {
        var repo = new PolicyDocumentRepository(mongo.Context);
        await repo.UpsertManyAsync([Policy("a", "Alpha", "old"), Policy("b", "Beta", "beta")], CancellationToken.None);
        await repo.UpsertManyAsync([Policy("a", "Alpha", "new")], CancellationToken.None);

        var removed = await repo.DeleteMissingAsync(["a", "wfh", "travel"], CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal("new", (await repo.GetAsync("a", CancellationToken.None))!.Content);
        Assert.Null(await repo.GetAsync("b", CancellationToken.None));
    }

    [Fact]
    public async Task Query_log_returns_only_the_callers_entries_newest_first()
    {
        var repo = new QueryLogRepository(mongo.Context);
        await repo.AddAsync(Log("user-1", "first", DateTimeOffset.UtcNow.AddMinutes(-5)), CancellationToken.None);
        await repo.AddAsync(Log("user-1", "second", DateTimeOffset.UtcNow), CancellationToken.None);
        await repo.AddAsync(Log("user-2", "other", DateTimeOffset.UtcNow), CancellationToken.None);

        var recent = await repo.GetRecentAsync("user-1", 10, CancellationToken.None);

        Assert.Equal(["second", "first"], recent.Select(r => r.Question));
        Assert.Equal(ToolInvocationOutcome.Blocked, recent[0].ToolCalls[0].Outcome);
    }

    private static PolicyDocument Policy(string id, string title, string content) =>
        new() { Id = id, Title = title, Content = content, SourceUpdatedAt = DateTimeOffset.UtcNow };

    private static QueryLogEntry Log(string user, string question, DateTimeOffset at) => new()
    {
        UserId = user,
        SessionId = "s1",
        Question = question,
        CreatedAt = at,
        ToolCalls = [new ToolInvocation("applyLeave", SourceSystem.ZohoPeople, ToolInvocationOutcome.Blocked, 0, "not on the allowlist")]
    };
}
