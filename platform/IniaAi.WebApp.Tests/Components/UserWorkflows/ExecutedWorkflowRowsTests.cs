using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.UserWorkflows.Common;

namespace IniaAi.WebApp.Tests.Components.UserWorkflows;

public class ExecutedWorkflowRowsTests
{
    private static readonly DateTime _now = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_CarriesTheOutcomeOfTheMatchingExecution()
    {
        Workflow workflow = NewWorkflow("w1");
        ExecutionOutcome outcome = Failed("w1", "CriticalBlockFailure", "Block 'Agent' failed");

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([workflow], [outcome]);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.Same(workflow, row.Workflow);
        Assert.Equal(WorkflowState.Failed, row.State);
        Assert.Equal("CriticalBlockFailure", row.FailureKind);
        Assert.Equal("Block 'Agent' failed", row.FailureDetail);
    }

    [Fact]
    public void Build_WithoutAnExecutionRow_ReportsUndefined_RatherThanInventingAState()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], []);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.Equal(WorkflowState.Undefined, row.State);
        Assert.Null(row.FailureKind);
        Assert.Null(row.FailureDetail);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public void Build_PreservesTheOrderItWasGiven()
    {
        Workflow first = NewWorkflow("w1");
        Workflow second = NewWorkflow("w2");

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([first, second], []);

        Assert.Equal(["w1", "w2"], rows.Select(r => r.Workflow?.Id));
    }

    [Fact]
    public void Build_WithSeveralExecutionsForOneSnapshot_TakesTheMostRecent()
    {
        Workflow workflow = NewWorkflow("w1");
        var older = new ExecutionOutcome(
            ExecutionId: "e-old",
            WorkflowId: "w1",
            CreatedAt: _now.AddHours(-2),
            State: WorkflowState.Cancelled,
            FailureKind: null,
            FailureDetail: null,
            WarningKind: null,
            WarningDetail: null,
            CompletedAt: _now.AddHours(-2));
        ExecutionOutcome newer = Failed("w1", "ResultsLost", "a persist failed") with
        {
            ExecutionId = "e-new",
            CreatedAt = _now,
        };

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([workflow], [older, newer]);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.Equal("e-new", row.ExecutionId);
        Assert.Equal(WorkflowState.Failed, row.State);
    }

    [Fact]
    public void Build_NeverAttachesAnExecutionToAnotherSnapshot()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1")],
            [Failed("w-other", "ResultsLost", "not mine")]);

        Assert.Equal(WorkflowState.Undefined, rows.Single(row => row.Workflow?.Id == "w1").State);
    }

    [Fact]
    public void Build_AFailedExecutionWithoutASnapshot_IsListedAsItsOwnRow()
    {
        ExecutionOutcome refused = Failed("w-never-written", "RejectedAtIntake", "Required document class 'Statement' is missing") with
        {
            Title = "Screened Report",
            WorkflowTypeId = "ScreenedType",
        };

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1")],
            [refused],
            new Dictionary<string, string> { ["ScreenedType"] = "Screened type" });

        ExecutedWorkflowRow row = Assert.Single(rows, row => row.Workflow is null);
        Assert.Equal("Screened Report", row.Name);
        Assert.Equal("Screened type", row.TypeName);
        Assert.Equal("ScreenedType", row.WorkflowTypeId);
        Assert.Equal(_now, row.CreatedAt);
        Assert.Equal(WorkflowState.Failed, row.State);
        Assert.Equal("RejectedAtIntake", row.AttentionKind);
        Assert.Equal("Required document class 'Statement' is missing", row.AttentionDetail);
    }

    [Fact]
    public void Build_AnExecutionThatDidNotFail_WithoutASnapshot_StaysOffTheList()
    {
        ExecutionOutcome completed = Failed("w-deleted", "unused", "unused") with { State = WorkflowState.Draft };

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], [completed]);

        Assert.Equal("w1", Assert.Single(rows).Workflow?.Id);
    }

    [Fact]
    public void Filter_FindsARunThatFailedBeforeItsSnapshot_ByNameAndCause()
    {
        ExecutionOutcome refused = Failed("w-never-written", "RejectedAtIntake", "Required document class 'Statement' is missing") with
        {
            Title = "Screened Report",
        };

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], [refused]);

        Assert.Null(Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "screened report")).Workflow);
        Assert.Null(Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "rejectedatintake")).Workflow);
    }

    [Fact]
    public void Filter_WithoutASearchTerm_KeepsEveryRow()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1"), NewWorkflow("w2")],
            []);

        Assert.Equal(2, ExecutedWorkflowRowFilter.Apply(rows, "  ").Count());
    }

    [Fact]
    public void Filter_MatchesTheStateLabel_SoFailedRunsCanBeFound()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1"), NewWorkflow("w2")],
            [Failed("w1", "CriticalBlockFailure", "Block 'Agent' failed")]);

        ExecutedWorkflowRow match = Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "failed"));
        Assert.Equal("w1", match.Workflow?.Id);
    }

    [Fact]
    public void Filter_MatchesTheFailureKindAndDetail_SoTriageCanSearchByCause()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1"), NewWorkflow("w2")],
            [Failed("w2", "LifetimeExceeded", "the host that was pumping it is presumed dead")]);

        Assert.Equal("w2", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "lifetimeexceeded")).Workflow?.Id);
        Assert.Equal("w2", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "presumed dead")).Workflow?.Id);
    }

    [Fact]
    public void Filter_StillMatchesTheColumnsItAlwaysDid()
    {
        Workflow named = NewWorkflow("w1");
        named.Name = "Greenvolt Credit Review";
        named.Description = "quarterly";
        Workflow other = NewWorkflow("w2");

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([named, other], []);

        Assert.Equal("w1", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "greenvolt")).Workflow?.Id);
        Assert.Equal("w1", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "quarterly")).Workflow?.Id);
        Assert.Equal("w1", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "2026-08-19")).Workflow?.Id);
    }

    [Fact]
    public void Attention_AWarningOnlyRow_ShowsTheWarning()
    {
        ExecutionOutcome outcome = Warned("w1", "UnverifiedRelease", "released unverified");

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], [outcome]);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.True(row.HasAttention);
        Assert.True(row.IsWarningOnly);
        Assert.Equal("UnverifiedRelease", row.AttentionKind);
        Assert.Equal("released unverified", row.AttentionDetail);
    }

    [Fact]
    public void Attention_AFailureBesideAWarning_WinsTheCell()
    {
        ExecutionOutcome outcome = Failed("w1", "CriticalBlockFailure", "Block 'Agent' failed") with
        {
            WarningKind = "UnverifiedRelease",
            WarningDetail = "released unverified",
        };

        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], [outcome]);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.True(row.HasAttention);
        Assert.False(row.IsWarningOnly);
        Assert.Equal("CriticalBlockFailure", row.AttentionKind);
        Assert.Equal("Block 'Agent' failed", row.AttentionDetail);
    }

    [Fact]
    public void Attention_ACleanRow_ShowsNothing()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build([NewWorkflow("w1")], []);

        ExecutedWorkflowRow row = Assert.Single(rows);
        Assert.False(row.HasAttention);
        Assert.False(row.IsWarningOnly);
        Assert.Null(row.AttentionKind);
        Assert.Null(row.AttentionDetail);
    }

    [Fact]
    public void Filter_MatchesTheWarningKindAndDetail_SoWarnedRunsCanBeFound()
    {
        IReadOnlyList<ExecutedWorkflowRow> rows = ExecutedWorkflowRows.Build(
            [NewWorkflow("w1"), NewWorkflow("w2")],
            [Warned("w2", "UnverifiedRelease", "still failing: totals recompute")]);

        Assert.Equal("w2", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "unverifiedrelease")).Workflow?.Id);
        Assert.Equal("w2", Assert.Single(ExecutedWorkflowRowFilter.Apply(rows, "totals recompute")).Workflow?.Id);
    }

    private static Workflow NewWorkflow(string id)
    {
        return new Workflow
        {
            Id = id,
            Name = id,
            Title = id,
            CreatedAt = id == "w1" ? _now : _now.AddDays(-40),
        };
    }

    private static ExecutionOutcome Warned(string workflowId, string kind, string detail)
    {
        return new ExecutionOutcome(
            ExecutionId: $"e-{workflowId}",
            WorkflowId: workflowId,
            CreatedAt: _now,
            State: WorkflowState.Draft,
            FailureKind: null,
            FailureDetail: null,
            WarningKind: kind,
            WarningDetail: detail,
            CompletedAt: _now);
    }

    private static ExecutionOutcome Failed(string workflowId, string kind, string detail)
    {
        return new ExecutionOutcome(
            ExecutionId: $"e-{workflowId}",
            WorkflowId: workflowId,
            CreatedAt: _now,
            State: WorkflowState.Failed,
            FailureKind: kind,
            FailureDetail: detail,
            WarningKind: null,
            WarningDetail: null,
            CompletedAt: _now);
    }
}
