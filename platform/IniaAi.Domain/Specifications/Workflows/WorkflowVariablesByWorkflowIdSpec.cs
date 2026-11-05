using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// The variable definitions of one workflow row. For an executed clone, the variables the run
/// was submitted against.
/// </summary>
public sealed class WorkflowVariablesByWorkflowIdSpec : Specification<WorkflowVariable>
{
    public WorkflowVariablesByWorkflowIdSpec(string workflowId)
    {
        _ = Query
            .Where(variable => variable.WorkflowId == workflowId)
            .AsNoTracking();
    }
}
