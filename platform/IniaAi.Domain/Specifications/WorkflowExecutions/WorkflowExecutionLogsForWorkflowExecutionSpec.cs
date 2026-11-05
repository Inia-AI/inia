using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowExecutionLogsForWorkflowExecutionSpec : Specification<WorkflowExecutionLog>
{
    public WorkflowExecutionLogsForWorkflowExecutionSpec(string workflowExecutionId)
    {
        _ = Query
            .Where(x => x.WorkflowExecutionId == workflowExecutionId)
            .Include(x => x.Block)
                .ThenInclude(x => x!.BlockResponse)
            .AsNoTracking();
    }
}
