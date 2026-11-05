using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class ReportSectionByIdSpec : Specification<ReportSection>, ISingleResultSpecification<ReportSection>
{
    public ReportSectionByIdSpec(string sectionId, string workflowExecutionId, string organizationId)
    {
        _ = Query
            .Where(s => s.Id == sectionId
                && s.WorkflowReport.WorkflowExecutionId == workflowExecutionId
                && s.WorkflowReport.OrganizationId == organizationId)
            .Include(s => s.Paragraphs);
    }
}
