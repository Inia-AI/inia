using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving a workflow execution by its Hangfire JobId.
/// No organization filter — callers must enforce tenant isolation.
/// Not marked <see cref="ISingleResultSpecification{T}"/> because <see cref="WorkflowExecution.JobId"/>
/// has no unique database constraint.
/// </summary>
public sealed class WorkflowExecutionByJobIdSpec : Specification<WorkflowExecution>
{
    public WorkflowExecutionByJobIdSpec(string jobId)
    {
        _ = Query
            .Where(x => x.JobId == jobId)
            .AsNoTracking();
    }
}
