using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Constants;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;

/// <summary>
/// Handles workflow execution initiation, including user validation,
/// document info persistence, job creation, and returning the resulting output DTO.
/// </summary>
public class StartWorkflowExecutionCommandHandler
    : IRequestHandler<StartWorkflowExecutionCommand, ErrorOr<WorkflowExecutionDto>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly ILogger<StartWorkflowExecutionCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartWorkflowExecutionCommandHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">Unit of work for user data operations.</param>
    /// <param name="coreUnitOfWork">Unit of work for core operations.</param>
    /// <param name="access">Decides whether the organization reaches the workflow's type.</param>
    /// <param name="orchestrator">The shared workflow execution orchestrator.</param>
    /// <param name="logger">Logger for operational diagnostics.</param>
    public StartWorkflowExecutionCommandHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowTypeAccess access,
        IWorkflowExecutionOrchestrator orchestrator,
        ILogger<StartWorkflowExecutionCommandHandler> logger)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _access = access;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Handles the execution logic for the <see cref="StartWorkflowExecutionCommand"/>.
    /// </summary>
    /// <param name="request">The command request containing workflow metadata and execution flags.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A result containing either a <see cref="WorkflowExecutionDto"/> representing the started workflow,
    /// or an error if the user or workflow cannot be found.
    /// </returns>
    public async Task<ErrorOr<WorkflowExecutionDto>> Handle(
        StartWorkflowExecutionCommand request,
        CancellationToken cancellationToken)
    {
        ErrorOr<Workflow> runnable = await RunnableWorkflowGuard.RunnableAsync(
            await GetWorkflowAsync(request.Request.WorkflowId, request.OrganizationId), request.OrganizationId, _access, _logger, cancellationToken);
        if (runnable.IsError)
        {
            return runnable.Errors;
        }

        Workflow workflow = runnable.Value;

        // Canonicalize inbound variable keys to the template's PascalCase ReplacementTags so a
        // mis-cased key (e.g. from an edit-and-re-run payload) still binds to its variable instead
        // of silently falling back to the template default. Unlike the submit path, Start does NOT
        // inject defaults for absent variables — it only normalizes the keys it was given.
        Dictionary<string, JsonElement> canonicalVariables = VariableKeyCanonicalizer.CanonicalizeVariableKeys(
            request.Request.Variables, workflow.Variables, _logger);

        (string emailIntakeMode, string? emailAttachmentsClass) = EmailIntakeModes.Resolve(
            request.Request.ExpandEmails, request.Request.AttachmentsDocumentClass);

        // The dormant regression path hands over captures already collected; the freeze option
        // names a source for the orchestrator to resolve. A start carries one or the other, and
        // naming both is a programming error rather than a silent choice between them.
        FreezeSource? freezeSource = FreezeSource.FromSubmissionOptions(request.Request.FreezeSourceExecutionId);

        if (request.ReplayOutputs is not null)
        {
            freezeSource = freezeSource is null
                ? new FreezeSource.ReplaySet(request.ReplayOutputs)
                : throw new InvalidOperationException(
                    "A start names the dormant regression replay set or the freeze source execution — never both.");
        }

        return await _orchestrator.ExecuteWorkflowAsync(
            workflow,
            request.Request.Files,
            canonicalVariables,
            request.OrganizationId,
            request.UserId,
            request.Username,
            request.Email,
            request.Request.CounterpartyId,
            request.Request.PeerCompanyIds,
            isConfidential: request.Request.IsConfidential,
            ct: cancellationToken,
            freezeSource: freezeSource,
            emailIntakeMode: emailIntakeMode,
            emailAttachmentsClass: emailAttachmentsClass,
            isRerun: request.IsRerun);
    }

    private async Task<Workflow?> GetWorkflowAsync(string workflowId, string organizationId)
    {
        return await _coreUnitOfWork.Workflows.GetByIdAsync(workflowId)
            ?? RunnableWorkflowGuard.OwnCopy(await _userDataUnitOfWork.Workflows.GetByIdAsync(workflowId), organizationId);
    }
}
