using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// A run starts only for a workflow whose type the caller's organization has access to. A type
/// the organization cannot reach answers exactly as an unknown one, so a caller cannot tell a
/// real type id from a made-up one.
/// </summary>
public static class RunnableWorkflowGuard
{
    public const string NOT_FOUND_MESSAGE = "Workflow not found.";

    public static async Task<ErrorOr<Workflow>> RunnableAsync(
        Workflow? workflow,
        string organizationId,
        ICoreUnitOfWork coreUnitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (workflow is null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(workflow.WorkflowTypeId)
            || !await WorkflowTypeAccessGuard.HasAccessAsync(coreUnitOfWork, organizationId, workflow.WorkflowTypeId, cancellationToken))
        {
            logger.LogWarning(
                "Organization {OrganizationId} asked to run workflow type {WorkflowTypeId}, which it has no access to",
                LogSanitizer.ForLog(organizationId),
                LogSanitizer.ForLog(workflow.WorkflowTypeId));

            return NotFound();
        }

        return workflow;
    }

    private static Error NotFound()
    {
        return Error.NotFound(description: NOT_FOUND_MESSAGE);
    }
}
