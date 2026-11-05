using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SharedComponentEntryCellVisualsTests
{
    [Fact]
    public void Live_IsUnemphasised_BecauseItIsTheExpectedState()
    {
        Assert.Equal(Color.Default, SharedComponentEntryCellVisuals.ColorFor(SharedComponentEntryCellState.Live));
    }

    [Theory]
    [InlineData(SharedComponentEntryCellState.Custom)]
    [InlineData(SharedComponentEntryCellState.Missing)]
    public void DeviationsThatStopTrackingTheHead_AreEmphasised(SharedComponentEntryCellState state)
    {
        Assert.NotEqual(Color.Default, SharedComponentEntryCellVisuals.ColorFor(state));
    }

    [Fact]
    public void EveryState_HasAColor()
    {
        foreach (SharedComponentEntryCellState state in Enum.GetValues<SharedComponentEntryCellState>())
        {
            _ = SharedComponentEntryCellVisuals.ColorFor(state);
        }
    }
}
