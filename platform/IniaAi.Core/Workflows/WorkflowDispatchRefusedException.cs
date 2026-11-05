using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Workflows;

/// <summary>
/// The dispatcher refused the workflow's shape before any block ran, so the run fails with no
/// block failure to report and the reason is recorded on the execution itself.
/// </summary>
public abstract class WorkflowDispatchRefusedException(string detail)
    : Exception(detail), IWorkflowExecutionFailure
{
    public abstract string FailureKind { get; }

    public string FailureDetail => Message;
}
