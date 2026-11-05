using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving a single workflow execution log with full details.
/// Includes Block and BlockResponse for complete content access.
/// </summary>
public sealed class WorkflowExecutionLogByIdAndExecutionSpec : Specification<WorkflowExecutionLog>
{
    public WorkflowExecutionLogByIdAndExecutionSpec(string logId, string workflowExecutionId)
    {
        _ = Query
            .Where(x => x.Id == logId && x.WorkflowExecutionId == workflowExecutionId)
            .Include(x => x.Block)
                .ThenInclude(x => x!.BlockResponse)
            .AsNoTracking();
    }
}
