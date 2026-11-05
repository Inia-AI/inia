using ErrorOr;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.WorkflowExecutions;

/// <summary>
/// Who may read a run's evidence (its executed workflow, its agent traces, its cost): a caller
/// whose role already admits every run in the organization, or the organization's service user
/// for a run it started itself, so a scripted test run can read back its own result. Any other
/// caller is refused, because the evidence carries the template's prompts. A run in another
/// organization is never found.
/// </summary>
public static class RunEvidenceAccess
{
    public const string NOT_FOUND_MESSAGE = "Workflow execution not found.";

    public const string ACCESS_DENIED_MESSAGE =
        "Only a staff role or the service user that started this run can read its evidence.";

    public static async Task<ErrorOr<WorkflowExecution>> ResolveAsync(
        IUserDataUnitOfWork unitOfWork,
        string organizationId,
        string userId,
        string executionId,
        bool callerReadsEveryRun,
        CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await unitOfWork.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(x => x.Id == executionId && x.OrganizationId == organizationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (execution is null)
        {
            return Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        ErrorOr<Success> allowed = await AuthorizeAsync(
            unitOfWork, organizationId, userId, execution, callerReadsEveryRun, cancellationToken);

        return allowed.IsError ? allowed.Errors : execution;
    }

    public static async Task<ErrorOr<Success>> AuthorizeAsync(
        IUserDataUnitOfWork unitOfWork,
        string organizationId,
        string userId,
        WorkflowExecution execution,
        bool callerReadsEveryRun,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(execution.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            return Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        if (callerReadsEveryRun)
        {
            return Result.Success;
        }

        if (!string.Equals(execution.UserId, userId, StringComparison.Ordinal))
        {
            return Error.Forbidden(description: ACCESS_DENIED_MESSAGE);
        }

        bool callerIsTheOrganizationsServiceUser = await unitOfWork.Repository<User>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId && user.IsSystem && user.OrganizationId == organizationId,
                cancellationToken);

        return callerIsTheOrganizationsServiceUser
            ? Result.Success
            : Error.Forbidden(description: ACCESS_DENIED_MESSAGE);
    }
}
