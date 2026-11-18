using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The workflow types an organization reaches: the ones its access rows grant, unless <see cref="IWorkflowTypeReach"/>
/// decides its types. Every read that decides access asks here, so no two reads can answer differently.
/// </summary>
public sealed class WorkflowTypeAccess
{
    private readonly ICoreUnitOfWork _core;
    private readonly IWorkflowTypeReach _reach;

    public WorkflowTypeAccess(ICoreUnitOfWork core, IWorkflowTypeReach reach)
    {
        _core = core;
        _reach = reach;
    }

    /// <summary>The ids of the workflow types the organization reaches, for a query to filter on.</summary>
    public async Task<IReadOnlyCollection<string>> TypeIdsAsync(string organizationId, CancellationToken cancellationToken)
    {
        return await _reach.TypeIdsAsync(organizationId, cancellationToken)
            ?? await Rows()
                .Where(access => access.OrganizationId == organizationId)
                .Select(access => access.WorkflowTypeId)
                .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanReachAsync(string organizationId, string workflowTypeId, CancellationToken cancellationToken)
    {
        if (await _reach.TypeIdsAsync(organizationId, cancellationToken) is { } decided)
        {
            return decided.Contains(workflowTypeId, StringComparer.Ordinal);
        }

        WorkflowTypeOrganizationAccess? granted = await _core.Repository<WorkflowTypeOrganizationAccess>()
            .GetFirstOrDefaultAsync(new WorkflowTypeAccessByOrgSpec(workflowTypeId, organizationId), cancellationToken);
        return granted is not null;
    }

    /// <summary>Whether an organization other than this one reaches the type, so the type is not this one's alone.</summary>
    public async Task<bool> IsReachedByAnotherAsync(string organizationId, string workflowTypeId, CancellationToken cancellationToken)
    {
        if (await _reach.IsReachedAsync(workflowTypeId, cancellationToken))
        {
            return true;
        }

        List<string> granted = await Rows()
            .Where(access => access.WorkflowTypeId == workflowTypeId && access.OrganizationId != organizationId)
            .Select(access => access.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (string other in granted)
        {
            if (await _reach.TypeIdsAsync(other, cancellationToken) is null)
            {
                return true;
            }
        }

        return false;
    }

    private IQueryable<WorkflowTypeOrganizationAccess> Rows()
    {
        return _core.Repository<WorkflowTypeOrganizationAccess>().GetAll().AsNoTracking();
    }
}
