using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowReportWithDetailsSpec : Specification<WorkflowReport>, ISingleResultSpecification<WorkflowReport>
{
    public WorkflowReportWithDetailsSpec(string workflowExecutionId, string organizationId)
    {
        _ = Query
            .Where(r => r.WorkflowExecutionId == workflowExecutionId
                && r.OrganizationId == organizationId)
            .Include(r => r.Sections.OrderBy(s => s.Order))
                .ThenInclude(s => s.Paragraphs.OrderBy(p => p.Order))
            .AsNoTracking();
    }
}
