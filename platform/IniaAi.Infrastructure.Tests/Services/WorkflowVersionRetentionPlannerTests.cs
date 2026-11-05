using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Infrastructure.Services;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the keep/trim decision: head, earliest, last-N, and the time window are always kept; only
/// payloads outside every keep rule are cleared; already-cleared and empty rows are skipped; the
/// per-run cap clears the oldest first. No database — the planner is a pure function of metadata.
/// </summary>
public class WorkflowVersionRetentionPlannerTests
{
    private const string T1 = "WT-One";
    private const string T2 = "WT-Two";

    private static WorkflowVersionRetentionPolicy Policy(
        int keepLastN = 5,
        int keepNewerThanDays = 10,
        int maxPerRun = 500)
    {
        return new WorkflowVersionRetentionPolicy(keepLastN, keepNewerThanDays, ReportOnly: false, maxPerRun);
    }

    private static WorkflowVersionMetadata V(
        DateTime now,
        string id,
        double ageDays,
        string type = T1,
        bool hasPayload = true,
        DateTime? trimmedAt = null)
    {
        return new WorkflowVersionMetadata(id, type, now.AddDays(-ageDays), $"hash-{id}", trimmedAt, hasPayload);
    }

    [Fact]
    public void Plan_NeverTrimsHeadOrEarliest()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "earliest", 100), V(now, "v2", 90), V(now, "v3", 80), V(now, "v4", 70),
            V(now, "v5", 60), V(now, "v6", 50), V(now, "v7", 40), V(now, "head", 30),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(), now);

        Assert.DoesNotContain("head", trimIds);
        Assert.DoesNotContain("earliest", trimIds);
    }

    [Fact]
    public void Plan_AllOld_KeepsLastNAndEarliest_TrimsTheMiddle()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "earliest", 100), V(now, "v2", 90), V(now, "v3", 80), V(now, "v4", 70),
            V(now, "v5", 60), V(now, "v6", 50), V(now, "v7", 40), V(now, "head", 30),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(keepLastN: 5, keepNewerThanDays: 10), now);

        Assert.Equal(["v2", "v3"], trimIds.OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Plan_AllWithinTimeWindow_TrimsNothing()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "v1", 8), V(now, "v2", 6), V(now, "v3", 4), V(now, "v4", 2), V(now, "v5", 1),
            V(now, "v6", 0.5), V(now, "v7", 0.2),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(keepLastN: 3, keepNewerThanDays: 180), now);

        Assert.Empty(trimIds);
    }

    [Fact]
    public void Plan_KeepLastNBelowFloor_StillKeepsMinimumFivePayloads()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "earliest", 100), V(now, "v2", 90), V(now, "v3", 80), V(now, "v4", 70),
            V(now, "v5", 60), V(now, "v6", 50), V(now, "v7", 40), V(now, "head", 30),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(keepLastN: 1, keepNewerThanDays: 0), now);

        Assert.Equal(2, trimIds.Count);
    }

    [Fact]
    public void Plan_IsPerType_OneTypesTrimDoesNotTouchAnother()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "a1", 100), V(now, "a2", 90), V(now, "a3", 80), V(now, "a4", 70),
            V(now, "a5", 60), V(now, "a6", 50), V(now, "a7", 40), V(now, "a8", 30),
            V(now, "b1", 100, type: T2), V(now, "b2", 90, type: T2), V(now, "b3", 80, type: T2),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(keepLastN: 5, keepNewerThanDays: 10), now);

        Assert.All(trimIds, id => Assert.StartsWith("a", id));
        Assert.DoesNotContain("b2", trimIds);
    }

    [Fact]
    public void Plan_SkipsAlreadyTrimmedAndEmptyRows()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "earliest", 100), V(now, "v2", 90, trimmedAt: now.AddDays(-1)), V(now, "v3", 80, hasPayload: false),
            V(now, "v4", 70), V(now, "v5", 60), V(now, "v6", 50), V(now, "v7", 40), V(now, "head", 30),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(versions, Policy(keepLastN: 5, keepNewerThanDays: 10), now);

        Assert.DoesNotContain("v2", trimIds);
        Assert.DoesNotContain("v3", trimIds);
        Assert.Empty(trimIds);
    }

    [Fact]
    public void Plan_CapsAtMaxPerRun_TrimmingOldestFirst()
    {
        DateTime now = DateTime.UtcNow;
        List<WorkflowVersionMetadata> versions =
        [
            V(now, "earliest", 100), V(now, "v2", 90), V(now, "v3", 80), V(now, "v4", 70),
            V(now, "v5", 60), V(now, "v6", 50), V(now, "v7", 40), V(now, "head", 30),
        ];

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(
            versions, Policy(keepLastN: 5, keepNewerThanDays: 10, maxPerRun: 1), now);

        Assert.Equal(["v2"], trimIds);
    }
}
