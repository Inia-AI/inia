using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowExecutionLogsWithBlockSpec : Specification<WorkflowExecutionLog>
{
    public WorkflowExecutionLogsWithBlockSpec(string workflowExecutionId, string? logId = null)
    {
        _ = Query
            .Where(x => x.WorkflowExecutionId == workflowExecutionId)
            .Include(x => x.Block);

        if (!string.IsNullOrEmpty(logId))
        {
            _ = Query.Where(x => x.Id == logId);
        }
    }
}
