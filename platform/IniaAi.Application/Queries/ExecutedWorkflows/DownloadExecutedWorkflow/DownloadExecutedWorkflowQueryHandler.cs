using System.Text;
using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.WorkflowExecutions;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

/// <summary>
/// Resolves an executed workflow snapshot from one of three lookup keys
/// (executionId, workflowId, or jobId), serializes the <see cref="Workflow"/>
/// to a pretty-printed JSON <c>.workflow</c> file with its run evidence
/// (<see cref="ExecutedWorkflowExport"/>), and returns the bytes.
/// Mirrors the SuperAdmin download in <c>IniaAi.WebApp/Components/UserWorkflows/Pages/Workflows.razor</c>.
/// </summary>
public class DownloadExecutedWorkflowQueryHandler
    : IRequestHandler<DownloadExecutedWorkflowQuery, ErrorOr<DownloadExecutedWorkflowResult>>
{
    private const string CONTENT_TYPE_JSON = "application/json";
    private const string FILE_EXTENSION = ".workflow";
    private const string NOT_FOUND_MESSAGE = "Executed workflow not found.";

    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<DownloadExecutedWorkflowQueryHandler> _logger;

    public DownloadExecutedWorkflowQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<DownloadExecutedWorkflowQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<DownloadExecutedWorkflowResult>> Handle(
        DownloadExecutedWorkflowQuery request,
        CancellationToken cancellationToken)
    {
        ErrorOr<string> resolved = await ResolveWorkflowIdAsync(request, cancellationToken);
        if (resolved.IsError)
        {
            return resolved.Errors;
        }

        if (!request.CallerReadsEveryRun)
        {
            ErrorOr<Success> allowed = await AuthorizeRunStarterAsync(request, resolved.Value, cancellationToken);
            if (allowed.IsError)
            {
                return allowed.Errors;
            }
        }

        Workflow? workflow = await _unitOfWork.Workflows
            .GetAll(new WorkflowExecutionDetailByIdSpec())
            .Where(x => x.Id == resolved.Value && x.OrganizationId == request.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken);

        if (workflow is null)
        {
            _logger.LogWarning(
                "User {UserId} requested download for missing workflow {WorkflowId}",
                request.UserId,
                resolved.Value);
            return Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        string json = await ExecutedWorkflowExport.ToJsonAsync(workflow, _unitOfWork, cancellationToken);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string fileName = $"{workflow.Name}{FILE_EXTENSION}";

        return new DownloadExecutedWorkflowResult
        {
            Content = bytes,
            ContentType = CONTENT_TYPE_JSON,
            FileName = fileName,
        };
    }

    /// <summary>
    /// A caller without a staff role reads only a run its own service user started. Checked
    /// before the snapshot loads, so a refused caller never makes the server read the graph.
    /// </summary>
    private async Task<ErrorOr<Success>> AuthorizeRunStarterAsync(
        DownloadExecutedWorkflowQuery request,
        string workflowId,
        CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await _unitOfWork.Repository<WorkflowExecution>()
            .GetAll()
            .AsNoTracking()
            .Where(x => x.WorkflowId == workflowId && x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        ErrorOr<Success> allowed = execution is null
            ? Error.Forbidden(description: RunEvidenceAccess.ACCESS_DENIED_MESSAGE)
            : await RunEvidenceAccess.AuthorizeAsync(
                _unitOfWork, request.OrganizationId, request.UserId, execution, callerReadsEveryRun: false, cancellationToken);

        if (allowed.IsError)
        {
            _logger.LogWarning(
                "User {UserId} may not download workflow {WorkflowId}: it holds no staff role and did not start the run as the service user",
                request.UserId,
                workflowId);
        }

        return allowed;
    }

    private async Task<ErrorOr<string>> ResolveWorkflowIdAsync(
        DownloadExecutedWorkflowQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            bool inOrganization = await _unitOfWork.Repository<Workflow>()
                .GetAll()
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.WorkflowId && x.OrganizationId == request.OrganizationId, cancellationToken);
            return inOrganization ? request.WorkflowId : Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        // Every lookup filters by organization inside the query, so a run of another
        // organization answers exactly as a run that does not exist.
        if (!string.IsNullOrWhiteSpace(request.ExecutionId))
        {
            WorkflowExecution? execution = await _unitOfWork.WorkflowExecutions
                .GetAll(new WorkflowExecutionByIdSpec(request.ExecutionId))
                .Where(x => x.OrganizationId == request.OrganizationId)
                .FirstOrDefaultAsync(cancellationToken);
            return ResolveFromExecution(execution, request, "ExecutionId", request.ExecutionId);
        }

        if (!string.IsNullOrWhiteSpace(request.JobId))
        {
            // JobId is not unique at the DB level, so filter by OrganizationId inside the
            // query — resolving across tenants and then checking the org would be both
            // non-deterministic and a cross-tenant information leak.
            WorkflowExecution? execution = await _unitOfWork.WorkflowExecutions
                .GetAll(new WorkflowExecutionByJobIdSpec(request.JobId))
                .Where(x => x.OrganizationId == request.OrganizationId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return ResolveFromExecution(execution, request, "JobId", request.JobId);
        }

        // Validator should have rejected this — defensive only.
        return Error.Validation(description: "No identifier provided.");
    }

    private ErrorOr<string> ResolveFromExecution(
        WorkflowExecution? execution,
        DownloadExecutedWorkflowQuery request,
        string lookupKey,
        string lookupValue)
    {
        if (execution is null)
        {
            _logger.LogWarning(
                "User {UserId} requested download by {LookupKey} {LookupValue}, but no execution was found",
                request.UserId,
                lookupKey,
                LogSanitizer.ForLog(lookupValue));
            return Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        if (string.IsNullOrWhiteSpace(execution.WorkflowId))
        {
            _logger.LogWarning(
                "Execution {ExecutionId} has no WorkflowId — cannot download (lookup by {LookupKey})",
                execution.Id,
                lookupKey);
            return Error.NotFound(description: NOT_FOUND_MESSAGE);
        }

        return execution.WorkflowId;
    }
}
