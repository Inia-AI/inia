using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowReportForExportSpec : Specification<WorkflowReport>, ISingleResultSpecification<WorkflowReport>
{
    public WorkflowReportForExportSpec(string workflowExecutionId)
    {
        _ = Query
            .Where(r => r.WorkflowExecutionId == workflowExecutionId)
            .Include(r => r.Counterparty)
            .Include(r => r.Sections.OrderBy(s => s.Order))
                .ThenInclude(s => s.Paragraphs.OrderBy(p => p.Order))
            .AsNoTracking();
    }
}
