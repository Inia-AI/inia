using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// Specification for the WorkflowTypeOrganizationAccess row granting an organization access to a
/// workflow type. WorkflowTypeAccess reads it for an organization whose access rows decide the types it
/// reaches; every other caller asks WorkflowTypeAccess.
/// </summary>
public sealed class WorkflowTypeAccessByOrgSpec
    : Specification<WorkflowTypeOrganizationAccess>, ISingleResultSpecification<WorkflowTypeOrganizationAccess>
{
    public WorkflowTypeAccessByOrgSpec(string workflowTypeId, string organizationId)
    {
        _ = Query
            .AsNoTracking()
            .Where(a => a.WorkflowTypeId == workflowTypeId && a.OrganizationId == organizationId);
    }
}
