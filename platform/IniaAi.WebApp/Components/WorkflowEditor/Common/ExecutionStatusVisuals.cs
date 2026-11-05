using IniaAi.Domain.Entities.Workflows.Enums;
using MudBlazor;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class ExecutionStatusVisuals
{
    public static string Icon(ExecutionStatus status) => status switch
    {
        ExecutionStatus.NotExecuted => Icons.Material.Outlined.RadioButtonUnchecked,
        ExecutionStatus.Running => Icons.Material.Outlined.Refresh,
        ExecutionStatus.Completed => Icons.Material.Filled.CheckCircle,
        ExecutionStatus.Failed => Icons.Material.Filled.Error,
        ExecutionStatus.Skipped => Icons.Material.Outlined.SkipNext,
        _ => Icons.Material.Outlined.RadioButtonUnchecked
    };

    public static Color Color(ExecutionStatus status) => status switch
    {
        ExecutionStatus.NotExecuted => MudBlazor.Color.Default,
        ExecutionStatus.Running => MudBlazor.Color.Primary,
        ExecutionStatus.Completed => MudBlazor.Color.Success,
        ExecutionStatus.Failed => MudBlazor.Color.Error,
        ExecutionStatus.Skipped => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default
    };

    public static string Label(ExecutionStatus status) => status switch
    {
        ExecutionStatus.NotExecuted => "Not Executed",
        ExecutionStatus.Running => "Running…",
        ExecutionStatus.Completed => "Completed",
        ExecutionStatus.Failed => "Failed",
        ExecutionStatus.Skipped => "Skipped",
        _ => "Unknown"
    };

    public static ExecutionStatus Aggregate(IEnumerable<ExecutionStatus> statuses)
    {
        int total = 0;
        int running = 0;
        int completed = 0;
        int failed = 0;
        int skipped = 0;

        foreach (ExecutionStatus status in statuses)
        {
            total++;
            switch (status)
            {
                case ExecutionStatus.Running:
                    running++;
                    break;
                case ExecutionStatus.Completed:
                    completed++;
                    break;
                case ExecutionStatus.Failed:
                    failed++;
                    break;
                case ExecutionStatus.Skipped:
                    skipped++;
                    break;
            }
        }

        return Aggregate(total, running, completed, failed, skipped);
    }

    public static ExecutionStatus Aggregate(int total, int running, int completed, int failed, int skipped)
    {
        if (total == 0)
        {
            return ExecutionStatus.NotExecuted;
        }

        if (failed > 0)
        {
            return ExecutionStatus.Failed;
        }

        if (running > 0)
        {
            return ExecutionStatus.Running;
        }

        if (completed + skipped == total)
        {
            return skipped > 0 ? ExecutionStatus.Skipped : ExecutionStatus.Completed;
        }

        return ExecutionStatus.NotExecuted;
    }
}
