using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// Specification for the WorkflowTypeOrganizationAccess row granting an organization access to a
/// workflow type. The presence of a row is the tenant-isolation boundary for templates, so callers
/// use this to gate any operation resolved from a caller-supplied WorkflowTypeId.
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
