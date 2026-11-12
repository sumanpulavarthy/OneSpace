using OneSpace.Domain.Assistant;
using OneSpace.Infrastructure.Persistence;

namespace OneSpace.IntegrationTests;

public sealed class ConversationStoreTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task Turns_are_appended_and_read_back_oldest_first()
    {
        var store = new ConversationStore(mongo.Context);
        var session = Guid.NewGuid().ToString("N");

        await store.AppendAsync("u1", session, Turn("one"), CancellationToken.None);
        await store.AppendAsync("u1", session, Turn("two"), CancellationToken.None);
        await store.AppendAsync("u1", session, Turn("three"), CancellationToken.None);

        var turns = await store.GetRecentTurnsAsync("u1", session, 2, CancellationToken.None);

        Assert.Equal(["two", "three"], turns.Select(t => t.Question));
        Assert.Equal(["zoho_getLeaveBalance"], turns[0].ToolNames);
    }

    [Fact]
    public async Task Another_employee_cannot_read_or_extend_a_session()
    {
        var store = new ConversationStore(mongo.Context);
        var session = Guid.NewGuid().ToString("N");
        await store.AppendAsync("owner", session, Turn("private"), CancellationToken.None);

        await store.AppendAsync("intruder", session, Turn("injected"), CancellationToken.None);

        Assert.Equal(["private"], (await store.GetRecentTurnsAsync("owner", session, 10, CancellationToken.None)).Select(t => t.Question));
        Assert.Equal(["injected"], (await store.GetRecentTurnsAsync("intruder", session, 10, CancellationToken.None)).Select(t => t.Question));
    }

    [Fact]
    public async Task Only_the_newest_turns_are_kept()
    {
        var store = new ConversationStore(mongo.Context);
        var session = Guid.NewGuid().ToString("N");
        for (var i = 0; i < ConversationStore.MaxStoredTurns + 5; i++)
            await store.AppendAsync("u1", session, Turn($"q{i}"), CancellationToken.None);

        var turns = await store.GetRecentTurnsAsync("u1", session, 100, CancellationToken.None);

        Assert.Equal(ConversationStore.MaxStoredTurns, turns.Count);
        Assert.Equal($"q{ConversationStore.MaxStoredTurns + 4}", turns[^1].Question);
    }

    private static ConversationTurn Turn(string question) =>
        new() { Question = question, Answer = $"answer to {question}", ToolNames = ["zoho_getLeaveBalance"] };
}
