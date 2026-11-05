using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;

namespace IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

/// <summary>
/// Query to download an executed workflow snapshot as a JSON <c>.workflow</c> file.
/// Exactly one of <see cref="ExecutionId"/>, <see cref="WorkflowId"/>, <see cref="JobId"/> must be supplied.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID for audit logging.</param>
/// <param name="ExecutionId">Optional: the WorkflowExecution.Id.</param>
/// <param name="WorkflowId">Optional: the Workflow.Id (the cloned execution snapshot).</param>
/// <param name="JobId">Optional: the WorkflowExecution.JobId (Hangfire job).</param>
public sealed record DownloadExecutedWorkflowQuery(
    string OrganizationId,
    string UserId,
    string? ExecutionId,
    string? WorkflowId,
    string? JobId) : IRequest<ErrorOr<DownloadExecutedWorkflowResult>>
{
    /// <summary>
    /// True when the caller's role admits every run in the organization (operator, super admin).
    /// False limits the caller to runs its own service user started (<c>RunEvidenceAccess</c>).
    /// </summary>
    public bool CallerReadsEveryRun { get; init; }
}
