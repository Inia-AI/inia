using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Text;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;

/// <summary>
/// Handles <see cref="ValidateDocumentClassesQuery"/> to validate document classes for a workflow.
/// </summary>
public sealed class ValidateDocumentClassesQueryHandler
    : IRequestHandler<ValidateDocumentClassesQuery, ErrorOr<DocumentClassValidationResultDto>>
{
    private readonly ILogger<ValidateDocumentClassesQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly IDocumentClassValidationService _validationService;

    public ValidateDocumentClassesQueryHandler(
        ILogger<ValidateDocumentClassesQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowTypeAccess access,
        IDocumentClassValidationService validationService)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _access = access;
        _validationService = validationService;
    }

    public async Task<ErrorOr<DocumentClassValidationResultDto>> Handle(
        ValidateDocumentClassesQuery request,
        CancellationToken cancellationToken)
    {
        // Fetch workflow (note: GetByIdAsync should include Parent and DocumentClasses via configuration)
        Workflow? workflow = await _coreUnitOfWork.Workflows.GetByIdAsync(request.WorkflowId);

        if (workflow?.WorkflowTypeId is null
            || !await _access.CanReachAsync(request.OrganizationId, workflow.WorkflowTypeId, cancellationToken))
        {
            _logger.LogWarning(
                "Workflow {WorkflowId} not found for organization {OrganizationId}",
                LogSanitizer.ForLog(request.WorkflowId),
                request.OrganizationId);

            return Error.NotFound(description: $"Workflow '{request.WorkflowId}' not found.");
        }

        // Fetch documents
        IEnumerable<Document> documents = await _userDataUnitOfWork.Documents
            .FindAsync(d => request.DocumentIds.Contains(d.Id) && d.OrganizationId == request.OrganizationId);

        if (documents.Count() != request.DocumentIds.Count)
        {
            var foundIds = documents.Select(d => d.Id).ToHashSet();
            var missingIds = request.DocumentIds.Except(foundIds).ToList();

            _logger.LogWarning(
                "Some documents not found: {MissingIds}",
                LogSanitizer.ForLog(string.Join(", ", missingIds)));

            return Error.NotFound(description: $"Documents not found: {string.Join(", ", missingIds)}");
        }

        // Validate document classes
        DocumentClassValidationResult validationResult = _validationService.ValidateDocumentClasses(workflow, documents);

        DocumentClassValidationResultDto resultDto = ToDto(validationResult);

        _logger.LogInformation(
            "Document class validation for workflow {WorkflowId}: IsValid={IsValid}, HasWarnings={HasWarnings}",
            LogSanitizer.ForLog(request.WorkflowId),
            validationResult.IsValid,
            validationResult.HasWarnings);

        return resultDto;
    }

    private static DocumentClassValidationResultDto ToDto(DocumentClassValidationResult result)
    {
        return new DocumentClassValidationResultDto
        {
            IsValid = result.IsValid,
            HasWarnings = result.HasWarnings,
            Errors = [.. result.Errors],
            Warnings = [.. result.Warnings],
            DocumentClassStatuses = [.. result.DocumentClassStatuses.Select(status => new DocumentClassStatusDto
            {
                Key = status.Key,
                DisplayName = status.DisplayName,
                Requirement = status.Requirement,
                ShouldBeSingular = status.ShouldBeSingular,
                UploadedCount = status.UploadedCount,
                IsSatisfied = status.IsSatisfied,
                ValidationMessage = status.ValidationMessage,
            })],
        };
    }
}
