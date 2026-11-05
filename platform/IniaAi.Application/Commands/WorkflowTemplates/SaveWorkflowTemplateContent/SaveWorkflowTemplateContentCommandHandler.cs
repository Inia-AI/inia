using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Common.Text;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

/// <summary>
/// Handles <see cref="SaveWorkflowTemplateContentCommand"/>. Validates the JSON structurally,
/// creates the workflow type and its organization access row when absent (upsert), and
/// delegates the save to <see cref="SaveWorkflowCommand"/> with optimistic concurrency enforced.
/// </summary>
public class SaveWorkflowTemplateContentCommandHandler
    : IRequestHandler<SaveWorkflowTemplateContentCommand, ErrorOr<SaveWorkflowTemplateContentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly WorkflowTemplateSavePreflight _preflight;
    private readonly ISender _sender;
    private readonly IGitHubWorkflowVersioningService _gitHubVersioning;
    private readonly IOptionsMonitor<GitHubSettings> _gitHubSettings;
    private readonly ILogger<SaveWorkflowTemplateContentCommandHandler> _logger;

    public SaveWorkflowTemplateContentCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IWorkflowTemplateStructuralValidator structuralValidator,
        WorkflowTemplateSavePreflight preflight,
        ISender sender,
        IGitHubWorkflowVersioningService gitHubVersioning,
        IOptionsMonitor<GitHubSettings> gitHubSettings,
        ILogger<SaveWorkflowTemplateContentCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _gitHubVersioning = gitHubVersioning ?? throw new ArgumentNullException(nameof(gitHubVersioning));
        _gitHubSettings = gitHubSettings ?? throw new ArgumentNullException(nameof(gitHubSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<SaveWorkflowTemplateContentResult>> Handle(
        SaveWorkflowTemplateContentCommand request,
        CancellationToken cancellationToken)
    {
        WorkflowStructuralValidationResult validation = _structuralValidator.Validate(request.WorkflowJson);
        if (!validation.IsValid)
        {
            return validation.Errors;
        }

        List<Error> referenceErrors = await _preflight.ValidateSharedComponentReferencesAsync(
            validation.Workflow!,
            cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return referenceErrors;
        }

        WorkflowTypePresence presence = await _preflight.ReadPresenceAsync(
            request.WorkflowTypeId,
            cancellationToken);
        if (presence == WorkflowTypePresence.Retired)
        {
            return WorkflowTypeErrorFactory.Deleted(request.WorkflowTypeId);
        }

        bool createdWorkflowType = false;
        WorkflowType? createdType = null;
        WorkflowTypeOrganizationAccess? createdAccess = null;

        if (presence == WorkflowTypePresence.Missing)
        {
            ErrorOr<string?> categoryResult = await _preflight.ResolveCategoryAsync(
                request.NewTypeCategoryId,
                cancellationToken);
            if (categoryResult.IsError)
            {
                return categoryResult.Errors;
            }

            createdType = new WorkflowType(request.WorkflowTypeId, request.NewTypeName ?? request.WorkflowTypeId)
            {
                Description = request.NewTypeDescription,
                CategoryId = categoryResult.Value,
            };
            createdAccess = new WorkflowTypeOrganizationAccess
            {
                WorkflowTypeId = request.WorkflowTypeId,
                OrganizationId = request.OrganizationId,
            };

            await _coreUnitOfWork.WorkflowTypes.AddAsync(createdType);
            await _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(createdAccess);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

            createdWorkflowType = true;

            _logger.LogInformation(
                "Created workflow type {WorkflowTypeId} for organization {OrganizationId} via template save",
                LogSanitizer.ForLog(request.WorkflowTypeId),
                request.OrganizationId);
        }

        ErrorOr<SaveWorkflowResult> saveResult = await _sender.Send(
            new SaveWorkflowCommand(
                request.OrganizationId,
                request.UserId,
                request.WorkflowTypeId,
                validation.Workflow!,
                request.BaseVersionId,
                EnforceOptimisticConcurrency: true,
                ConfirmSignatureFieldDeletions: request.ConfirmSignatureFieldDeletions),
            cancellationToken);

        if (saveResult.IsError)
        {
            // Keep "create type" atomic with "type has content": if the save we just delegated to
            // failed, roll back the type + access row we created so a failed upsert leaves no
            // orphan empty type behind. Best-effort — a cleanup failure must not mask the real error.
            if (createdWorkflowType)
            {
                await TryRemoveOrphanTypeAsync(createdType!, createdAccess!, cancellationToken);
            }

            return saveResult.Errors;
        }

        // Mirror the saved template to the GitHub workflow repository so API saves stay in
        // sync with the mirror (which previously only the Blazor editor committed to). Skip a
        // deduped save — the content is unchanged so there is nothing new to commit.
        if (!saveResult.Value.WasDeduped)
        {
            await TryMirrorToGitHubAsync(request, validation.Workflow!, cancellationToken);
        }

        return new SaveWorkflowTemplateContentResult(
            saveResult.Value.VersionId,
            saveResult.Value.ContentHash,
            saveResult.Value.WasDeduped,
            createdWorkflowType,
            [.. validation.Warnings, .. WorkflowStructuralValidationWarning.FromSaveWarnings(saveResult.Value.Warnings)],
            saveResult.Value.SignatureSync);
    }

    private async Task TryMirrorToGitHubAsync(
        SaveWorkflowTemplateContentCommand request,
        Workflow workflow,
        CancellationToken cancellationToken)
    {
        // Best-effort: the save is already persisted, so a mirror failure (e.g. GitHub
        // unavailable, or no PAT configured in this environment) must never fail the save.
        try
        {
            GitHubCommitResult result = await _gitHubVersioning.CommitWorkflowAsync(
                workflowTypeId: request.WorkflowTypeId,
                area: string.Empty,
                function: string.Empty,
                content: workflow.ToExportJson(),
                commitMessage: $"Save workflow {request.WorkflowTypeId}",
                authorName: _gitHubSettings.CurrentValue.ApiAuthorName,
                authorEmail: _gitHubSettings.CurrentValue.ApiAuthorEmail,
                cancellationToken: cancellationToken);

            // An empty Sha means the mirror was a no-op (disabled), so don't claim it committed.
            if (!string.IsNullOrEmpty(result.Sha))
            {
                _logger.LogInformation(
                    "Mirrored workflow template {WorkflowTypeId} to GitHub ({Sha})",
                    LogSanitizer.ForLog(request.WorkflowTypeId),
                    result.Sha);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to mirror workflow template {WorkflowTypeId} to GitHub; the save succeeded and the mirror was skipped",
                LogSanitizer.ForLog(request.WorkflowTypeId));
        }
    }

    private async Task TryRemoveOrphanTypeAsync(
        WorkflowType createdType,
        WorkflowTypeOrganizationAccess createdAccess,
        CancellationToken cancellationToken)
    {
        try
        {
            _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().Remove(createdAccess);
            _coreUnitOfWork.WorkflowTypes.Remove(createdType);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The orphan is harmless (empty type, access scoped to the caller's own org) and a
            // retry reuses it via the existing-type path, so a failed cleanup is logged, not thrown.
            _logger.LogWarning(
                ex,
                "Failed to roll back orphan workflow type {WorkflowTypeId} after a failed upsert save",
                LogSanitizer.ForLog(createdType.Id));
        }
    }
}
