using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowExecutionWithDetailsSpec : Specification<WorkflowExecution>
{
    public WorkflowExecutionWithDetailsSpec(string id)
    {
        _ = Query
            .Where(x => x.Id == id)
            .Include(x => x.WorkflowExecutionsCounterparties)
            .AsNoTracking();
    }
}
