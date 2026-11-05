using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;

/// <summary>
/// Request to transition a workflow execution to a new state.
/// Currently only <see cref="WorkflowState.Cancelled"/> is supported.
/// </summary>
public class UpdateWorkflowExecutionStateRequest
{
    /// <summary>The target state for the workflow execution.</summary>
    public WorkflowState State { get; set; }
}
