using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Workflows;

/// <summary>
/// Thrown when the execution row reached a terminal state by someone else's hand — a cross-host
/// cancel, a janitor — while this engine was still running it. The row is not ours any more:
/// unlike every other abort, a fenced run records nothing about itself, because any write would
/// contradict the state the winner already published. The caller stops and cleans up.
/// </summary>
public sealed class WorkflowExecutionFencedException : Exception
{
    public WorkflowExecutionFencedException(string workflowExecutionId, WorkflowState? observedState = null)
        : base(BuildMessage(workflowExecutionId, observedState))
    {
        WorkflowExecutionId = workflowExecutionId;
        ObservedState = observedState;
    }

    public string WorkflowExecutionId { get; }

    /// <summary>
    /// The terminal state the winner published, when the fence came from reading the row at a
    /// block boundary. Null when the fence came from losing a conditional write, which reports
    /// only that no active row matched.
    /// </summary>
    public WorkflowState? ObservedState { get; }

    private static string BuildMessage(string workflowExecutionId, WorkflowState? observedState)
    {
        return observedState is null
            ? $"Execution '{workflowExecutionId}' rejected this run's state write because it is no longer active; stopping without writing anything further."
            : $"Execution '{workflowExecutionId}' was already {observedState} when this run reached a block boundary; stopping without writing anything further.";
    }
}
