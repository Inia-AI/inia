using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunLimitsCacheTests
{
    private static readonly AgentRunLimits _limits = new(
        new AgentRunFuseLimit(900_000m, 900_000m, "milliseconds"),
        new AgentRunFuseLimit(1.25m, 2.5m, "usd"),
        new AgentRunFuseLimit(300m, 300m, "turns"),
        new AgentRunFuseLimit(134_217_728m, 276_824_064m, "bytes"));

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    [Fact]
    public void BeforeAnyFetch_NothingIsKnownAndNothingIsEnforceable()
    {
        var cache = new AgentRunLimitsCache(new FakeTime());

        Assert.Null(cache.LastKnown);
        Assert.Null(cache.Enforceable);
    }

    [Fact]
    public void AfterAStore_TheSnapshotIsKnownAndEnforceable()
    {
        var time = new FakeTime();
        var cache = new AgentRunLimitsCache(time);

        cache.Store(_limits);

        Assert.NotNull(cache.LastKnown);
        Assert.Equal(_limits, cache.LastKnown.Limits);
        Assert.Equal(time.Now, cache.LastKnown.FetchedAt);
        Assert.Equal(_limits, cache.Enforceable);
    }

    [Fact]
    public void AStaleSnapshot_StaysKnownForDisplay_ButIsNoLongerEnforceable()
    {
        var time = new FakeTime();
        var cache = new AgentRunLimitsCache(time);
        cache.Store(_limits);

        time.Now += AgentRunLimitsCache.FreshnessWindow + TimeSpan.FromSeconds(1);

        Assert.NotNull(cache.LastKnown);
        Assert.Null(cache.Enforceable);
    }

    [Fact]
    public void ARefreshedSnapshot_BecomesEnforceableAgain()
    {
        var time = new FakeTime();
        var cache = new AgentRunLimitsCache(time);
        cache.Store(_limits);
        time.Now += AgentRunLimitsCache.FreshnessWindow + TimeSpan.FromSeconds(1);

        cache.Store(_limits);

        Assert.Equal(_limits, cache.Enforceable);
        Assert.Equal(time.Now, cache.LastKnown!.FetchedAt);
    }
}
