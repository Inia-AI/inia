using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Infrastructure.Services;

namespace IniaAi.Infrastructure.Tests.Services;

public class LiveNarrationTidyStateTests
{
    private static readonly DateTimeOffset _origin = new(2026, 8, 13, 13, 39, 0, TimeSpan.Zero);

    private sealed class ScriptedTidier : IAgentNarrationTidier
    {
        private readonly int _failFirstCalls;
        public int Calls { get; private set; }
        public List<int> BatchSizes { get; } = [];
        public List<string> SeenKeys { get; } = [];

        public ScriptedTidier(int failFirstCalls = 0)
        {
            _failFirstCalls = failFirstCalls;
        }

        public Task<IReadOnlyDictionary<string, string?>> TidyAsync(
            IReadOnlyList<AgentNarrationLine> lines,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            BatchSizes.Add(lines.Count);
            SeenKeys.AddRange(lines.Select(static line => line.Key));
            IReadOnlyDictionary<string, string?> result = Calls <= _failFirstCalls
                ? new Dictionary<string, string?>()
                : lines.ToDictionary(static line => line.Key, static line => (string?)$"tidied {line.Key}");
            return Task.FromResult(result);
        }
    }

    private static List<AgentNarrationLine> Lines(params string[] keys)
    {
        return [.. keys.Select((key, index) =>
            new AgentNarrationLine(key, _origin.AddSeconds(index * 10), $"raw {key}"))];
    }

    [Fact]
    public async Task ATransientFailure_LeavesLinesUnsettledSoTheyRenderRawThenUpgrade()
    {
        var tidier = new ScriptedTidier(failFirstCalls: 1);
        var state = new LiveNarrationTidyState();

        await state.ApplyAsync(Lines("a", "b"), tidier, TestContext.Current.CancellationToken);
        Assert.Empty(state.SettledNarration);

        await state.ApplyAsync(Lines("a", "b", "c"), tidier, TestContext.Current.CancellationToken);
        Assert.Equal("tidied a", state.SettledNarration["a"]);
        Assert.Equal("tidied b", state.SettledNarration["b"]);
        Assert.Equal("tidied c", state.SettledNarration["c"]);
    }

    [Fact]
    public async Task SettledLines_AreNeverSentAgainAndNeverReword()
    {
        var tidier = new ScriptedTidier();
        var state = new LiveNarrationTidyState();

        await state.ApplyAsync(Lines("a", "b"), tidier, TestContext.Current.CancellationToken);
        string? settledA = state.SettledNarration["a"];

        await state.ApplyAsync(Lines("a", "b", "c"), tidier, TestContext.Current.CancellationToken);

        Assert.Equal(settledA, state.SettledNarration["a"]);
        Assert.Equal([2, 1], tidier.BatchSizes);
    }

    [Fact]
    public async Task APermanentFailure_SettlesRawAfterTheAttemptBudgetAndStopsCalling()
    {
        var tidier = new ScriptedTidier(failFirstCalls: int.MaxValue);
        var state = new LiveNarrationTidyState();

        for (int attempt = 0; attempt < LiveNarrationTidyState.MAX_TIDY_ATTEMPTS; attempt++)
        {
            await state.ApplyAsync(Lines("a"), tidier, TestContext.Current.CancellationToken);
        }

        Assert.Equal("raw a", state.SettledNarration["a"]);
        int callsAtSettle = tidier.Calls;

        await state.ApplyAsync(Lines("a"), tidier, TestContext.Current.CancellationToken);
        Assert.Equal(callsAtSettle, tidier.Calls);
    }

    [Fact]
    public async Task ToolLines_SettleOnceAndAreNeverRetidiedAsTheRunGrows()
    {
        var tidier = new ScriptedTidier();
        var state = new LiveNarrationTidyState();
        AgentNarrationLine note = new("n1:0", _origin, "Both years are present.");
        AgentNarrationLine firstCall = new("t1:1", _origin.AddSeconds(5), "Convert the annual reports")
        {
            Kind = AgentActivityLineKind.ToolAction,
        };
        AgentNarrationLine secondCall = new("t2:2", _origin.AddSeconds(9), "Check out directory contents")
        {
            Kind = AgentActivityLineKind.ToolAction,
        };

        await state.ApplyAsync([note, firstCall], tidier, TestContext.Current.CancellationToken);
        await state.ApplyAsync([note, firstCall, secondCall], tidier, TestContext.Current.CancellationToken);
        await state.ApplyAsync([note, firstCall, secondCall], tidier, TestContext.Current.CancellationToken);

        Assert.Equal(["n1:0", "t1:1", "t2:2"], tidier.SeenKeys);
        Assert.Equal([2, 1], tidier.BatchSizes);
        Assert.Equal("tidied t2:2", state.SettledNarration["t2:2"]);
    }

    [Fact]
    public async Task WithoutATidier_LinesSettleRawImmediately()
    {
        var state = new LiveNarrationTidyState();

        await state.ApplyAsync(Lines("a"), null, TestContext.Current.CancellationToken);

        Assert.Equal("raw a", state.SettledNarration["a"]);
    }
}
