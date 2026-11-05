namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// A block-level exception that must abort the whole run even when the block itself is
/// non-critical — a verdict-driven stop like verification's unsatisfiable, never an
/// infrastructure error. Deliberately separate from <see cref="IWorkflowExecutionFailure"/>:
/// classifying a failure for the execution row must not silently grant an exception the power
/// to void every remaining block's spend.
/// </summary>
public interface IAbortsWorkflowRun : IWorkflowExecutionFailure
{
}
