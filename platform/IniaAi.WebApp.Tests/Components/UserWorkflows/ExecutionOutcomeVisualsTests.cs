using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.WebApp.Components.UserWorkflows.Common;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.UserWorkflows;

public class ExecutionOutcomeVisualsTests
{
    [Theory]
    [InlineData(WorkflowState.Running, "Running")]
    [InlineData(WorkflowState.Draft, "Draft")]
    [InlineData(WorkflowState.Confirmed, "Confirmed")]
    [InlineData(WorkflowState.Failed, "Failed")]
    [InlineData(WorkflowState.Cancelled, "Cancelled")]
    [InlineData(WorkflowState.Queued, "Queued")]
    [InlineData(WorkflowState.ProcessingDocuments, "Processing Documents")]
    public void Label_NamesEveryStateTheTicketAsksFor(WorkflowState state, string expected)
    {
        Assert.Equal(expected, ExecutionOutcomeVisuals.Label(state));
    }

    [Fact]
    public void Label_ForUndefined_SaysUnknown_BecauseNoExecutionRowWasFound()
    {
        Assert.Equal("Unknown", ExecutionOutcomeVisuals.Label(WorkflowState.Undefined));
    }

    [Fact]
    public void Label_CoversEveryState_SoANewOneCannotRenderBlank()
    {
        foreach (WorkflowState state in Enum.GetValues<WorkflowState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ExecutionOutcomeVisuals.Label(state)));
        }
    }

    [Fact]
    public void Color_MarksFailedAsError()
    {
        Assert.Equal(Color.Error, ExecutionOutcomeVisuals.Color(WorkflowState.Failed));
    }

    [Theory]
    [InlineData(WorkflowState.Draft)]
    [InlineData(WorkflowState.Confirmed)]
    public void Color_MarksProducedReportsAsSuccess(WorkflowState state)
    {
        Assert.Equal(Color.Success, ExecutionOutcomeVisuals.Color(state));
    }

    [Fact]
    public void Color_DistinguishesCancelledFromFailed_BecauseTheyAreDifferentOutcomes()
    {
        Assert.NotEqual(
            ExecutionOutcomeVisuals.Color(WorkflowState.Failed),
            ExecutionOutcomeVisuals.Color(WorkflowState.Cancelled));
    }

    [Fact]
    public void CompletedAtLabel_IsEmpty_WhileTheRunHasNotFinished()
    {
        Assert.Equal(string.Empty, ExecutionOutcomeVisuals.CompletedAtLabel(null));
    }

    [Fact]
    public void CompletedAtLabel_ShowsWhenTheRunFinished_SoACancelledRowSaysWhen()
    {
        var completedAt = new DateTime(2026, 8, 19, 14, 5, 0, DateTimeKind.Utc);

        Assert.Equal("2026-08-19 14:05", ExecutionOutcomeVisuals.CompletedAtLabel(completedAt));
    }

    [Fact]
    public void Tooltip_NamesTheStateAndWhenItFinished()
    {
        var completedAt = new DateTime(2026, 8, 19, 14, 5, 0, DateTimeKind.Utc);

        string tooltip = ExecutionOutcomeVisuals.Tooltip(
            WorkflowState.Cancelled, completedAt, hasExecution: true);

        Assert.Contains("Cancelled", tooltip, StringComparison.Ordinal);
        Assert.Contains("2026-08-19 14:05", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Tooltip_ForAStillRunningExecution_NamesOnlyTheState()
    {
        Assert.Equal(
            "Running",
            ExecutionOutcomeVisuals.Tooltip(WorkflowState.Running, null, hasExecution: true));
    }

    [Fact]
    public void Tooltip_WithoutAnExecutionRow_SaysSo_RatherThanImplyingTheRunIsUnfinished()
    {
        string tooltip = ExecutionOutcomeVisuals.Tooltip(
            WorkflowState.Undefined, null, hasExecution: false);

        Assert.Contains("Unknown", tooltip, StringComparison.Ordinal);
        Assert.Contains("no execution", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Icon_CoversEveryState()
    {
        foreach (WorkflowState state in Enum.GetValues<WorkflowState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ExecutionOutcomeVisuals.Icon(state)));
        }
    }
}
