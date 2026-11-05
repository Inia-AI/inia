using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;

namespace IniaAi.Application.Common.Workflows;

public static class WorkflowTypeAccessGuard
{
    public static async Task<bool> HasAccessAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string organizationId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        WorkflowTypeOrganizationAccess? access = await coreUnitOfWork
            .Repository<WorkflowTypeOrganizationAccess>()
            .GetFirstOrDefaultAsync(
                new WorkflowTypeAccessByOrgSpec(workflowTypeId, organizationId),
                cancellationToken);

        return access is not null;
    }
}
