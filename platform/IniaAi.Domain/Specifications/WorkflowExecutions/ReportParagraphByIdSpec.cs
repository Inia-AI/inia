using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class ReportParagraphByIdSpec : Specification<ReportParagraph>, ISingleResultSpecification<ReportParagraph>
{
    public ReportParagraphByIdSpec(string paragraphId, string workflowExecutionId, string organizationId)
    {
        _ = Query
            .Where(p => p.Id == paragraphId
                && p.Section.WorkflowReport.WorkflowExecutionId == workflowExecutionId
                && p.Section.WorkflowReport.OrganizationId == organizationId);
    }
}
