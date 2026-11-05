using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowExecutionByIdSpec : Specification<WorkflowExecution>
{
    public WorkflowExecutionByIdSpec(string id)
    {
        _ = Query
            .Where(x => x.Id == id)
            .Include(x => x.WorkflowExecutionsCounterparties)
            .AsNoTracking();
    }
}