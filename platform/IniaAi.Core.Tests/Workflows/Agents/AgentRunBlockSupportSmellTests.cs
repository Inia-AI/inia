using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Agents;

/// <summary>
/// Pins the fabrication smell stamps and the round-wide ledger read they feed: a run is dated by the
/// runtime's own monitor clock, the cheap bar is read off the row that bought the run rather than
/// off everything the block spent getting there, and a round's spend panel sees every run bought
/// where current-work reads still see only the settled one.
/// </summary>
public class AgentRunBlockSupportSmellTests
{
    private const string FIRST_RUN_ID = "wf_b_w_r1_1";
    private const string SECOND_RUN_ID = "wf_b_w_r1_2";
    private const string THIRD_RUN_ID = "wf_b_w_r1_3";
    private const string SECOND_ROUND_RUN_ID = "wf_b_w_r2_1";
    private const string UNLEDGERED_RUN_ID = "wf_b_w_r9_1";

    /// <summary>
    /// The earlier row cost nothing, so it would carry the cheap stamp if the smell write reached
    /// past the run the monitor is about — its untouched state is what this pins.
    /// </summary>
    [Fact]
    public void RecordRunSmell_StampsTheSettledRunsRow_AndLeavesEveryOtherRunAlone()
    {
        (Block block, List<AgentRunPass> ledger) = ArrangeLedgeredBlock();
        AgentRunAttempt earlier = Attempt(1, FIRST_RUN_ID, AgentRunStatus.Failed, 0m);
        AgentRunAttempt settled = Attempt(2, SECOND_RUN_ID, AgentRunStatus.Succeeded, 0.004m);
        RecordSettled(ledger, 1, earlier, settled);

        AgentRunBlockSupport.RecordRunSmell(ledger, ConclusionOf(settled, 12, earlier, settled));

        AgentRunPass stamped = Assert.Single(ledger, pass => pass.ExecutionId == SECOND_RUN_ID);
        Assert.Equal(12d, stamped.DurationSeconds);
        Assert.True(stamped.SuspiciouslyFast);
        Assert.True(stamped.SuspiciouslyCheap);

        AgentRunPass untouched = Assert.Single(ledger, pass => pass.ExecutionId == FIRST_RUN_ID);
        Assert.Null(untouched.DurationSeconds);
        Assert.False(untouched.SuspiciouslyFast);
        Assert.False(untouched.SuspiciouslyCheap);

        Assert.Equal(2, AgentRunBlockSupport.PassesForRound(block, 1).Count);
    }

    [Fact]
    public void RecordRunSmell_ARunThatTookItsTimeAndSpent_IsStampedSuspiciousOnNeitherBar()
    {
        (Block block, List<AgentRunPass> ledger) = ArrangeLedgeredBlock();
        AgentRunAttempt settled = Attempt(1, FIRST_RUN_ID, AgentRunStatus.Succeeded, 1.25m);
        RecordSettled(ledger, 1, settled);

        AgentRunBlockSupport.RecordRunSmell(ledger, ConclusionOf(settled, 300, settled));

        AgentRunPass stamped = Assert.Single(ledger);
        Assert.Equal(300d, stamped.DurationSeconds);
        Assert.False(stamped.SuspiciouslyFast);
        Assert.False(stamped.SuspiciouslyCheap);
        Assert.NotNull(AgentRunBlockSupport.SettledPassFor(block, 1));
    }

    /// <summary>
    /// An editor re-run wipes the ledger while a run of the previous one is still settling, so a
    /// conclusion naming a run no row knows is a live shape, not a wiring bug.
    /// </summary>
    [Fact]
    public void RecordRunSmell_AConclusionForARunTheLedgerDoesNotKnow_StampsNothingAndDoesNotThrow()
    {
        (Block block, List<AgentRunPass> ledger) = ArrangeLedgeredBlock();
        AgentRunAttempt settled = Attempt(1, FIRST_RUN_ID, AgentRunStatus.Succeeded, 0.004m);
        RecordSettled(ledger, 1, settled);
        AgentRunAttempt stranger = Attempt(1, UNLEDGERED_RUN_ID, AgentRunStatus.Succeeded, 0m);

        AgentRunBlockSupport.RecordRunSmell(ledger, ConclusionOf(stranger, 3, stranger));

        AgentRunPass untouched = Assert.Single(AgentRunBlockSupport.PassesForRound(block, 1));
        Assert.Equal(FIRST_RUN_ID, untouched.ExecutionId);
        Assert.Null(untouched.DurationSeconds);
        Assert.False(untouched.SuspiciouslyFast);
        Assert.False(untouched.SuspiciouslyCheap);
    }

    /// <summary>
    /// The bars are per leg: a block that burned a dear failed pass before a near-free settled one
    /// still has a cheap run to declare, so the total the block spent must not launder it.
    /// </summary>
    [Fact]
    public void RecordRunSmell_ReadsTheCheapBarOffTheRow_NotTheTotalTheBlockSpentGettingThere()
    {
        (Block block, List<AgentRunPass> ledger) = ArrangeLedgeredBlock();
        AgentRunAttempt dearFailure = Attempt(1, FIRST_RUN_ID, AgentRunStatus.Failed, 0.60m);
        AgentRunAttempt settled = Attempt(2, SECOND_RUN_ID, AgentRunStatus.Succeeded, 0.004m);
        RecordSettled(ledger, 1, dearFailure, settled);
        AgentRunConclusion conclusion = ConclusionOf(settled, 300, dearFailure, settled);

        Assert.True(conclusion.Attempts.TotalCostUsd() > AgentRunBlockSupport.SUSPICIOUSLY_CHEAP_USD);

        AgentRunBlockSupport.RecordRunSmell(ledger, conclusion);

        AgentRunPass stamped = Assert.Single(
            AgentRunBlockSupport.PassesForRound(block, 1), pass => pass.ExecutionId == SECOND_RUN_ID);
        Assert.True(stamped.SuspiciouslyCheap);
        Assert.False(stamped.SuspiciouslyFast);
    }

    [Fact]
    public void PassesForRound_AnswersEveryRunOfTheRound_WhereSettledPassForAnswersOnlyTheSettledOne()
    {
        (Block block, List<AgentRunPass> ledger) = ArrangeLedgeredBlock();
        RecordSettled(
            ledger,
            1,
            Attempt(1, FIRST_RUN_ID, AgentRunStatus.Failed, 0.10m),
            Attempt(2, SECOND_RUN_ID, AgentRunStatus.Succeeded, 0.40m));
        ledger.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 3,
            ExecutionId = THIRD_RUN_ID,
            Unacknowledged = true,
        });
        RecordSettled(ledger, 2, Attempt(1, SECOND_ROUND_RUN_ID, AgentRunStatus.Succeeded, 0.70m));

        IReadOnlyList<AgentRunPass> roundOne = AgentRunBlockSupport.PassesForRound(block, 1);

        Assert.Equal(
            [FIRST_RUN_ID, SECOND_RUN_ID, THIRD_RUN_ID],
            roundOne.Select(static pass => pass.ExecutionId));
        Assert.Equal(0.50m, roundOne.Sum(static pass => pass.EstimatedCostUsd));

        AgentRunPass settled = Assert.IsType<AgentRunPass>(AgentRunBlockSupport.SettledPassFor(block, 1));
        Assert.Equal(SECOND_RUN_ID, settled.ExecutionId);

        Assert.Equal(
            SECOND_ROUND_RUN_ID,
            Assert.Single(AgentRunBlockSupport.PassesForRound(block, 2)).ExecutionId);
    }

    /// <summary>
    /// One bar, one input, so the two records of a settled run cannot disagree about it: the
    /// verifier's judgment and that run's ledger row both read the seconds off the monitor and
    /// the cheap bar off the cost they are handed. The cost that matters is the settled run's
    /// own — a dear failed pass before it says nothing about whether the answer that settled came
    /// back too cheap to be real.
    /// </summary>
    [Fact]
    public void SmellOf_DatesTheRunFromTheMonitor_AndReadsTheCheapBarOffTheCostItIsGiven()
    {
        AgentRunAttempt settled = Attempt(2, SECOND_RUN_ID, AgentRunStatus.Succeeded, 0.004m);
        AgentRunConclusion conclusion = ConclusionOf(
            settled, 12, Attempt(1, FIRST_RUN_ID, AgentRunStatus.Failed, 0.60m), settled);

        AgentRunSmell settledRun = AgentRunBlockSupport.SmellOf(conclusion, settled.EstimatedCostUsd);
        AgentRunSmell wholeBlock = AgentRunBlockSupport.SmellOf(conclusion, conclusion.Attempts.TotalCostUsd());

        Assert.Equal(12d, settledRun.Seconds);
        Assert.Equal(settledRun.Seconds, wholeBlock.Seconds);
        Assert.True(settledRun.SuspiciouslyFast);
        Assert.True(settledRun.SuspiciouslyCheap);
        Assert.False(wholeBlock.SuspiciouslyCheap);
    }

    /// <summary>
    /// What a round cost the group is the sum of every block's rows for that round — the
    /// verifier's own leg and each member's redo leg together. No single block carries the
    /// figure, which is why the criterion record's verifier-pass cost is not it, and why the
    /// group's spend panel reads the ledgers rather than any one column.
    /// </summary>
    [Fact]
    public void ARoundsSpend_IsTheSumOfEveryGroupBlocksRowsForThatRound()
    {
        var section = new Section("group", new Workflow("group"));
        List<AgentRunPass> verifierLedger = LedgerOf(new Block(BlockTypes.Verification, section, "verifier"));
        List<AgentRunPass> firstMemberLedger = LedgerOf(new Block(BlockTypes.Agent, section, "memberA"));
        List<AgentRunPass> secondMemberLedger = LedgerOf(new Block(BlockTypes.Agent, section, "memberB"));

        RecordSettled(verifierLedger, 1, Attempt(1, "wf_v_v_r1_1", AgentRunStatus.Succeeded, 0.30m));
        RecordSettled(firstMemberLedger, 1, Attempt(1, "wf_a_w_r1_1", AgentRunStatus.Succeeded, 0.10m));
        RecordSettled(secondMemberLedger, 1, Attempt(1, "wf_b_w_r1_1", AgentRunStatus.Succeeded, 0.15m));
        RecordSettled(verifierLedger, 2, Attempt(1, "wf_v_v_r2_1", AgentRunStatus.Succeeded, 0.40m));
        RecordSettled(firstMemberLedger, 2, Attempt(1, "wf_a_w_r2_1", AgentRunStatus.Succeeded, 0.20m));

        List<AgentRunPass> groupLedger = [.. verifierLedger, .. firstMemberLedger, .. secondMemberLedger];

        Assert.Equal(0.55m, RoundSpend(groupLedger, 1));
        Assert.Equal(0.60m, RoundSpend(groupLedger, 2));
        Assert.Equal(1.15m, groupLedger.Sum(static pass => pass.EstimatedCostUsd));

        // The verifier's own round-two leg is a third of what that round cost the group.
        Assert.Equal(0.40m, RoundSpend(verifierLedger, 2));
    }

    [Fact]
    public void PassesForRound_ABlockThatNeverExecuted_IsEmptyRatherThanAThrow()
    {
        var block = new Block(BlockTypes.Agent, new Section("bare", new Workflow("bare")));

        Assert.Empty(AgentRunBlockSupport.PassesForRound(block, 1));
        Assert.Null(AgentRunBlockSupport.SettledPassFor(block, 1));
    }

    private static decimal RoundSpend(IEnumerable<AgentRunPass> ledger, int round)
    {
        return ledger.Where(pass => pass.Round == round).Sum(static pass => pass.EstimatedCostUsd);
    }

    private static List<AgentRunPass> LedgerOf(Block block)
    {
        block.SetExecutionState(false, []);

        return ((IAgentRunProperties)block.Properties).RunLedger!;
    }

    private static (Block Block, List<AgentRunPass> Ledger) ArrangeLedgeredBlock()
    {
        var block = new Block(BlockTypes.Agent, new Section("smell", new Workflow("smell")));
        block.SetExecutionState(false, []);
        AgentProperties properties = Assert.IsType<AgentProperties>(block.Properties);

        return (block, Assert.IsType<AgentPropertiesExecutionState>(properties.ExecutionState).RunPasses);
    }

    private static AgentRunAttempt Attempt(int pass, string executionId, AgentRunStatus status, decimal costUsd)
    {
        return new AgentRunAttempt(
            pass,
            executionId,
            status,
            status == AgentRunStatus.Failed ? AgentRunSubtypes.INTERNAL_ERROR : null,
            costUsd,
            false);
    }

    private static void RecordSettled(List<AgentRunPass> ledger, int round, params AgentRunAttempt[] attempts)
    {
        foreach (AgentRunAttempt attempt in attempts)
        {
            AgentRunBlockSupport.RecordSettledPass(ledger, attempt, round, redoLeg: null);
        }
    }

    /// <summary>
    /// The monitor a settled run comes back on: its clock is the only duration the platform has,
    /// and it prices that one run, not the passes before it.
    /// </summary>
    private static AgentRunConclusion ConclusionOf(
        AgentRunAttempt settled,
        double durationSeconds,
        params AgentRunAttempt[] attempts)
    {
        var startedAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        return new AgentRunConclusion(
            new AgentRunMonitor
            {
                ExecutionId = settled.ExecutionId,
                Status = settled.Status,
                Attempt = settled.Pass,
                CreatedAt = startedAt,
                UpdatedAt = startedAt.AddSeconds(durationSeconds),
                EstimatedCostUsd = settled.EstimatedCostUsd,
            },
            null,
            false,
            attempts);
    }
}
