using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// Stitches executed workflow snapshots to the outcomes of the executions that produced them.
/// The two live in separate tables with no foreign key between them —
/// <c>WorkflowExecution.WorkflowId</c> is the only bridge — so the join happens here, in memory,
/// over one read of each side.
/// </summary>
public static class ExecutedWorkflowRows
{
    /// <summary>
    /// Builds one row per workflow, in the order given, attaching the most recent outcome that
    /// names it. A workflow no outcome names keeps a null outcome. A failed execution whose
    /// snapshot is not among <paramref name="workflows"/> follows as its own row: a run refused
    /// before its first block never writes one, and a failed run whose snapshot was deleted is
    /// still a run until it is deleted itself. An execution that did not fail stays off the list
    /// without a snapshot, so deleting the snapshot of a finished run removes it from the list.
    /// </summary>
    public static IReadOnlyList<ExecutedWorkflowRow> Build(
        IEnumerable<Workflow> workflows,
        IEnumerable<ExecutionOutcome> outcomes,
        IReadOnlyDictionary<string, string>? typeNamesById = null)
    {
        Dictionary<string, ExecutionOutcome> latestByWorkflowId = [];

        foreach (ExecutionOutcome outcome in outcomes)
        {
            if (string.IsNullOrEmpty(outcome.WorkflowId))
            {
                continue;
            }

            if (latestByWorkflowId.TryGetValue(outcome.WorkflowId, out ExecutionOutcome? existing)
                && existing.CreatedAt >= outcome.CreatedAt)
            {
                continue;
            }

            latestByWorkflowId[outcome.WorkflowId] = outcome;
        }

        List<Workflow> snapshots = [.. workflows];
        HashSet<string> snapshotIds = [.. snapshots.Select(workflow => workflow.Id)];

        return [
            .. snapshots.Select(workflow => new ExecutedWorkflowRow
            {
                Workflow = workflow,
                Outcome = latestByWorkflowId.GetValueOrDefault(workflow.Id),
                TypeName = workflow.WorkflowType?.Name ?? TypeNameOf(workflow.WorkflowTypeId, typeNamesById),
            }),
            .. latestByWorkflowId.Values
                .Where(outcome => outcome.State is WorkflowState.Failed && !snapshotIds.Contains(outcome.WorkflowId))
                .Select(outcome => new ExecutedWorkflowRow
                {
                    Outcome = outcome,
                    TypeName = TypeNameOf(outcome.WorkflowTypeId, typeNamesById),
                }),
        ];
    }

    private static string? TypeNameOf(string? workflowTypeId, IReadOnlyDictionary<string, string>? typeNamesById)
    {
        return workflowTypeId is not null && typeNamesById is not null && typeNamesById.TryGetValue(workflowTypeId, out string? name)
            ? name
            : null;
    }
}
