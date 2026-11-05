using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class ExecutionStatusVisualsTests
{
    [Theory]
    [InlineData(ExecutionStatus.NotExecuted, "Not Executed")]
    [InlineData(ExecutionStatus.Running, "Running…")]
    [InlineData(ExecutionStatus.Completed, "Completed")]
    [InlineData(ExecutionStatus.Failed, "Failed")]
    [InlineData(ExecutionStatus.Skipped, "Skipped")]
    public void Label_ReturnsExpectedText(ExecutionStatus status, string expected)
    {
        Assert.Equal(expected, ExecutionStatusVisuals.Label(status));
    }

    [Theory]
    [InlineData(ExecutionStatus.NotExecuted, Color.Default)]
    [InlineData(ExecutionStatus.Running, Color.Primary)]
    [InlineData(ExecutionStatus.Completed, Color.Success)]
    [InlineData(ExecutionStatus.Failed, Color.Error)]
    [InlineData(ExecutionStatus.Skipped, Color.Warning)]
    public void Color_ReturnsExpectedColor(ExecutionStatus status, Color expected)
    {
        Assert.Equal(expected, ExecutionStatusVisuals.Color(status));
    }

    [Fact]
    public void Icon_MapsEachStatusToItsMaterialIcon()
    {
        Assert.Equal(Icons.Material.Outlined.RadioButtonUnchecked, ExecutionStatusVisuals.Icon(ExecutionStatus.NotExecuted));
        Assert.Equal(Icons.Material.Outlined.Refresh, ExecutionStatusVisuals.Icon(ExecutionStatus.Running));
        Assert.Equal(Icons.Material.Filled.CheckCircle, ExecutionStatusVisuals.Icon(ExecutionStatus.Completed));
        Assert.Equal(Icons.Material.Filled.Error, ExecutionStatusVisuals.Icon(ExecutionStatus.Failed));
        Assert.Equal(Icons.Material.Outlined.SkipNext, ExecutionStatusVisuals.Icon(ExecutionStatus.Skipped));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, ExecutionStatus.NotExecuted)]
    [InlineData(3, 0, 3, 0, 0, ExecutionStatus.Completed)]
    [InlineData(3, 0, 2, 1, 0, ExecutionStatus.Failed)]
    [InlineData(3, 1, 2, 0, 0, ExecutionStatus.Running)]
    [InlineData(4, 0, 2, 0, 2, ExecutionStatus.Skipped)]
    [InlineData(2, 0, 0, 0, 2, ExecutionStatus.Skipped)]
    [InlineData(3, 0, 1, 0, 0, ExecutionStatus.NotExecuted)]
    public void Aggregate_FromCounts_ResolvesByPriority(
        int total, int running, int completed, int failed, int skipped, ExecutionStatus expected)
    {
        Assert.Equal(expected, ExecutionStatusVisuals.Aggregate(total, running, completed, failed, skipped));
    }

    [Fact]
    public void Aggregate_FromCounts_FailedTakesPrecedenceOverRunning()
    {
        Assert.Equal(ExecutionStatus.Failed, ExecutionStatusVisuals.Aggregate(total: 5, running: 1, completed: 1, failed: 1, skipped: 1));
    }

    [Fact]
    public void Aggregate_FromCounts_RunningTakesPrecedenceOverPartialCompletion()
    {
        Assert.Equal(ExecutionStatus.Running, ExecutionStatusVisuals.Aggregate(total: 3, running: 1, completed: 1, failed: 0, skipped: 1));
    }

    [Fact]
    public void Aggregate_FromStatuses_Empty_ReturnsNotExecuted()
    {
        Assert.Equal(ExecutionStatus.NotExecuted, ExecutionStatusVisuals.Aggregate(Array.Empty<ExecutionStatus>()));
    }

    [Fact]
    public void Aggregate_FromStatuses_AllCompleted_ReturnsCompleted()
    {
        ExecutionStatus[] statuses = [ExecutionStatus.Completed, ExecutionStatus.Completed];

        Assert.Equal(ExecutionStatus.Completed, ExecutionStatusVisuals.Aggregate(statuses));
    }

    [Fact]
    public void Aggregate_FromStatuses_AnyFailed_ReturnsFailed()
    {
        ExecutionStatus[] statuses = [ExecutionStatus.Completed, ExecutionStatus.Failed, ExecutionStatus.Running];

        Assert.Equal(ExecutionStatus.Failed, ExecutionStatusVisuals.Aggregate(statuses));
    }

    [Fact]
    public void Aggregate_FromStatuses_CompletedAndSkipped_ReturnsSkipped()
    {
        ExecutionStatus[] statuses = [ExecutionStatus.Completed, ExecutionStatus.Skipped];

        Assert.Equal(ExecutionStatus.Skipped, ExecutionStatusVisuals.Aggregate(statuses));
    }

    [Fact]
    public void Aggregate_FromStatuses_CompletedAndNotExecuted_ReturnsNotExecuted()
    {
        ExecutionStatus[] statuses = [ExecutionStatus.Completed, ExecutionStatus.NotExecuted];

        Assert.Equal(ExecutionStatus.NotExecuted, ExecutionStatusVisuals.Aggregate(statuses));
    }

    [Fact]
    public void Aggregate_FromStatuses_RunningWithoutFailure_ReturnsRunning()
    {
        ExecutionStatus[] statuses = [ExecutionStatus.Running, ExecutionStatus.Completed];

        Assert.Equal(ExecutionStatus.Running, ExecutionStatusVisuals.Aggregate(statuses));
    }
}
