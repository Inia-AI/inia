using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Infrastructure.Services;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunActivityFeedTests
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string EXECUTION_ID = "wfexec-1";
    private const string RUN_ID = "wfexec-1_block-1_w_1";

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private readonly ManualTimeProvider _time = new();
    private readonly AgentRunActivityFeed _feed;

    public AgentRunActivityFeedTests()
    {
        _feed = new AgentRunActivityFeed(_time);
    }

    private static AgentRunActivityRegistration Registration(
        string runId = RUN_ID,
        string orgId = ORG_ID,
        string executionId = EXECUTION_ID)
    {
        return AgentRunActivityRegistration.ForBlock(runId, orgId, executionId, "block-1", "Agent Block");
    }

    private static AgentActivityDigest Digest(int beats)
    {
        return new AgentActivityDigest
        {
            Beats = [.. Enumerable.Range(0, beats)
                .Select(static index => new AgentActivityBeat(
                    index, 0, AgentActivityBeatKind.Normal, $"beat {index}", null))],
        };
    }

    [Fact]
    public void SnapshotDeliversTheLatestPublishedDigest()
    {
        _feed.BeginRun(Registration());
        _feed.PublishDigest(RUN_ID, Digest(2));
        _feed.PublishDigest(RUN_ID, Digest(5));

        AgentRunActivityEntry entry = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
        Assert.Equal(5, entry.Digest?.Beats.Count);
        Assert.False(entry.Settled);
        Assert.Equal("block-1", entry.Registration.BlockId);
    }

    [Fact]
    public void SnapshotIsOrgScopedByTheRegistrationsOwnStamp()
    {
        _feed.BeginRun(Registration());

        Assert.Empty(_feed.SnapshotForExecution(OTHER_ORG_ID, EXECUTION_ID));
        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, "someone-elses-execution"));
    }

    [Fact]
    public void PublishForAnUnregisteredRunIsDropped()
    {
        _feed.PublishDigest(RUN_ID, Digest(3));

        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void ARunThatSettlesStaysReadableForTheGraceWindowThenExpires()
    {
        _feed.BeginRun(Registration());
        _feed.PublishDigest(RUN_ID, Digest(4));
        _feed.ConcludeRun(RUN_ID);

        _time.Now = _time.Now.AddMinutes(5);
        AgentRunActivityEntry entry = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
        Assert.True(entry.Settled);
        Assert.Equal(4, entry.Digest?.Beats.Count);

        _time.Now = _time.Now.AddMinutes(10);
        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void AnAbandonedRegistrationExpiresAtTheHardAgeBound()
    {
        _feed.BeginRun(Registration());

        _time.Now = _time.Now.AddMinutes(61);

        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void EntriesOfOneExecutionComeBackInRegistrationOrder()
    {
        _feed.BeginRun(Registration(runId: "run-a"));
        _time.Now = _time.Now.AddSeconds(1);
        _feed.BeginRun(Registration(runId: "run-b"));

        IReadOnlyList<AgentRunActivityEntry> entries = _feed.SnapshotForExecution(ORG_ID, EXECUTION_ID);

        Assert.Equal(
            ["run-a", "run-b"],
            entries.Select(static entry => entry.Registration.RunExecutionId).ToArray());
    }

    [Fact]
    public void ARunThatKeepsPublishingOutlivesTheAgeBoundCountedFromRegistration()
    {
        _feed.BeginRun(Registration());

        _time.Now = _time.Now.AddMinutes(55);
        _feed.PublishDigest(RUN_ID, Digest(3));

        _time.Now = _time.Now.AddMinutes(55);
        AgentRunActivityEntry entry = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
        Assert.Equal(3, entry.Digest?.Beats.Count);
        Assert.False(entry.Settled);

        _time.Now = _time.Now.AddMinutes(5);
        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void AnAbandonedRegistrationStillDiesAtTheBoundAPublishingRunSurvives()
    {
        _feed.BeginRun(Registration());

        _time.Now = _time.Now.AddMinutes(59);
        _ = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));

        _time.Now = _time.Now.AddMinutes(1);
        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void ConcludeRunRefreshesTheTouchAndTheSettledEntryExpiresOnRetention()
    {
        _feed.BeginRun(Registration());
        _feed.PublishDigest(RUN_ID, Digest(4));

        _time.Now = _time.Now.AddMinutes(59);
        _feed.ConcludeRun(RUN_ID);

        _time.Now = _time.Now.AddMinutes(5);
        AgentRunActivityEntry entry = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
        Assert.True(entry.Settled);
        Assert.Equal(4, entry.Digest?.Beats.Count);

        _time.Now = _time.Now.AddMinutes(5);
        Assert.Empty(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
    }

    [Fact]
    public void SnapshotCarriesTheRoundEachRunWasRegisteredIn()
    {
        _feed.BeginRun(AgentRunActivityRegistration.ForBlock(
            "run-round-1", ORG_ID, EXECUTION_ID, "block-1", "Agent Block", 1));
        _time.Now = _time.Now.AddSeconds(1);
        _feed.BeginRun(AgentRunActivityRegistration.ForBlock(
            "run-round-2", ORG_ID, EXECUTION_ID, "block-1", "Agent Block", 2));

        IReadOnlyList<AgentRunActivityEntry> entries = _feed.SnapshotForExecution(ORG_ID, EXECUTION_ID);

        Assert.Equal([1, 2], entries.Select(static entry => entry.Registration.Round).ToArray());
    }

    [Fact]
    public void ARegistrationThatNamesNoRoundIsRoundOne()
    {
        _feed.BeginRun(Registration());

        AgentRunActivityEntry entry = Assert.Single(_feed.SnapshotForExecution(ORG_ID, EXECUTION_ID));
        Assert.Equal(1, entry.Registration.Round);
    }
}
