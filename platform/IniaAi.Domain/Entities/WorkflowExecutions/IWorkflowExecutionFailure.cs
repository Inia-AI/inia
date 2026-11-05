namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Implemented by exceptions that classify themselves for the execution row, so the job layer can
/// carry a failure's kind and explanation onto <see cref="WorkflowExecution.FailureKind"/> and
/// <see cref="WorkflowExecution.FailureDetail"/> without knowing the concrete exception types.
/// </summary>
public interface IWorkflowExecutionFailure
{
    /// <summary>
    /// One of <see cref="WorkflowExecutionFailureKinds"/>, or a kind a workflow handler defines for
    /// its own workflow type.
    /// </summary>
    string FailureKind { get; }

    /// <summary>Human-readable explanation, truncated to the column limit at write time.</summary>
    string FailureDetail { get; }
}
