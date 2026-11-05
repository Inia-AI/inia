using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Resolves the workflow types a user chose to see. This is a display filter that reduces clutter,
/// never an access boundary — an empty result means the user chose none, which shows everything
/// their organization can reach. Callers must keep enforcing organization access separately, so
/// that a chosen type can only ever narrow what the organization already reaches.
/// </summary>
public static class UserWorkflowTypeVisibilityResolver
{
    public static async Task<List<string>> ResolveChosenTypeIdsAsync(
        IUserDataUnitOfWork userDataUnitOfWork,
        string userId,
        CancellationToken cancellationToken)
    {
        return await userDataUnitOfWork.Repository<UserWorkflowTypeVisibility>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => v.WorkflowTypeId)
            .ToListAsync(cancellationToken);
    }
}
