using System.Globalization;
using IniaAi.Domain.Entities.WorkflowExecutions;
using MudBlazor;

namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// How a persisted <see cref="WorkflowState"/> reads in the executed-workflows list.
/// <see cref="WorkflowState.Undefined"/> means no execution row was found for the snapshot, so it
/// reads "Unknown" rather than borrowing another state's label.
/// </summary>
public static class ExecutionOutcomeVisuals
{
    private const string COMPLETED_AT_FORMAT = "yyyy-MM-dd HH:mm";

    /// <summary>The state's display name.</summary>
    public static string Label(WorkflowState state) => state switch
    {
        WorkflowState.Running => "Running",
        WorkflowState.Draft => "Draft",
        WorkflowState.Confirmed => "Confirmed",
        WorkflowState.Failed => "Failed",
        WorkflowState.ProcessingDocuments => "Processing Documents",
        WorkflowState.Queued => "Queued",
        WorkflowState.Cancelled => "Cancelled",
        _ => "Unknown",
    };

    /// <summary>The state's chip colour: produced-a-report is success, failed is error.</summary>
    public static Color Color(WorkflowState state) => state switch
    {
        WorkflowState.Running => MudBlazor.Color.Primary,
        WorkflowState.Draft => MudBlazor.Color.Success,
        WorkflowState.Confirmed => MudBlazor.Color.Success,
        WorkflowState.Failed => MudBlazor.Color.Error,
        WorkflowState.ProcessingDocuments => MudBlazor.Color.Primary,
        WorkflowState.Queued => MudBlazor.Color.Info,
        WorkflowState.Cancelled => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default,
    };

    /// <summary>
    /// When the execution reached its terminal state, formatted for the row — empty while it has
    /// not. This is the "when" a Cancelled or Failed row carries.
    /// </summary>
    public static string CompletedAtLabel(DateTime? completedAt)
    {
        return completedAt is null
            ? string.Empty
            : completedAt.Value.ToString(COMPLETED_AT_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The state chip's hover text: the state, plus when it finished. A snapshot with no execution
    /// row says so, rather than reading as a run that simply has not finished.
    /// </summary>
    public static string Tooltip(WorkflowState state, DateTime? completedAt, bool hasExecution)
    {
        string label = Label(state);

        if (!hasExecution)
        {
            return $"{label} — no execution row was found for this snapshot";
        }

        return completedAt is null
            ? label
            : $"{label} at {CompletedAtLabel(completedAt)} UTC";
    }

    /// <summary>The state's icon.</summary>
    public static string Icon(WorkflowState state) => state switch
    {
        WorkflowState.Running => Icons.Material.Outlined.Refresh,
        WorkflowState.Draft => Icons.Material.Outlined.CheckCircle,
        WorkflowState.Confirmed => Icons.Material.Filled.CheckCircle,
        WorkflowState.Failed => Icons.Material.Filled.Error,
        WorkflowState.ProcessingDocuments => Icons.Material.Outlined.Description,
        WorkflowState.Queued => Icons.Material.Outlined.Schedule,
        WorkflowState.Cancelled => Icons.Material.Outlined.Cancel,
        _ => Icons.Material.Outlined.HelpOutline,
    };
}
