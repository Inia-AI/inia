using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;
using MudBlazor;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class SharedComponentEntryCellVisuals
{
    public static Color ColorFor(SharedComponentEntryCellState state)
    {
        return state switch
        {
            SharedComponentEntryCellState.Custom => Color.Warning,
            SharedComponentEntryCellState.Missing => Color.Error,
            SharedComponentEntryCellState.Hidden => Color.Secondary,
            SharedComponentEntryCellState.Added => Color.Info,
            _ => Color.Default,
        };
    }
}
