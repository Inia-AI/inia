using ErrorOr;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Text;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Email;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;

/// <summary>
/// Handles the submit workflow execution command.
/// Uploads files, merges variable defaults, and delegates to the shared orchestrator. A
/// submission with any failed file is rejected as a whole: the response names each refused
/// file by its index, an email the platform cannot convert ends the upload under its own code,
/// and every document the request created is removed again. A document the upload reused
/// stays, because it belongs to an earlier run. An email to expand in a build that reads no
/// email is uploaded as it is, so the intake refuses it for its format as it refuses any upload.
/// </summary>
public class SubmitWorkflowExecutionCommandHandler
    : IRequestHandler<SubmitWorkflowExecutionCommand, ErrorOr<WorkflowExecutionDto>>
{
    private readonly ILogger<SubmitWorkflowExecutionCommandHandler> _logger;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ISender _sender;
    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly IEmailExpansionService _emailExpansionService;
    private readonly ICaseFreezeSources _caseFreezeSources;
    private readonly DocumentIntakeGate _intakeGate;
    private readonly RunIntake _runIntake;

    public SubmitWorkflowExecutionCommandHandler(
        ILogger<SubmitWorkflowExecutionCommandHandler> logger,
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        ISender sender,
        IWorkflowExecutionOrchestrator orchestrator,
        IEmailExpansionService emailExpansionService,
        ICaseFreezeSources caseFreezeSources,
        DocumentIntakeGate intakeGate,
        RunIntake runIntake)
    {
        _logger = logger;
        _coreUnitOfWork = coreUnitOfWork;
        _userDataUnitOfWork = userDataUnitOfWork;
        _sender = sender;
        _orchestrator = orchestrator;
        _emailExpansionService = emailExpansionService;
        _caseFreezeSources = caseFreezeSources;
        _intakeGate = intakeGate;
        _runIntake = runIntake;
    }

    public async Task<ErrorOr<WorkflowExecutionDto>> Handle(
        SubmitWorkflowExecutionCommand request,
        CancellationToken cancellationToken)
    {
        // A retry is answered with the run it already has. Its key comes from the header or from the
        // emails it expands, so only a request with neither can be refused before its files are stored.
        if (_runIntake.IsPaused && request.IdempotencyKey is null && request.Metadata.Files?.Any(file => file.ExpandEmail) != true)
        {
            return RunIntake.Refusal();
        }

        // 1. Fetch workflow
        ErrorOr<Workflow> runnable = await RunnableWorkflowGuard.RunnableAsync(
            await GetWorkflowAsync(request.Metadata.WorkflowTypeId, request.OrganizationId), request.OrganizationId, _coreUnitOfWork, _logger, cancellationToken);
        if (runnable.IsError)
        {
            return runnable.Errors;
        }

        Workflow workflow = runnable.Value;

        // 2. Merge variable defaults from workflow definition
        Dictionary<string, JsonElement> effectiveVariables = MergeVariableDefaults(
            workflow, request.Metadata.Variables);

        FreezeSource? freezeSource = FreezeSource.FromSubmissionOptions(
            request.Metadata.FreezeSourceExecutionId,
            request.Metadata.FreezeSourceCaseId);

        if (freezeSource is FreezeSource.Case && _caseFreezeSources.Serves() is { IsError: true } refusal)
        {
            return refusal.Errors;
        }

        // 3. Upload each file, expanding emails where requested
        var uploads = new SubmissionUploads();
        var uploadErrors = new List<Error>();
        var intake = new EmailIntakeState();

        for (int i = 0; i < request.Files.Count; i++)
        {
            SubmitFileMetadata? fileMetadata = request.Metadata.Files is not null && i < request.Metadata.Files.Count
                ? request.Metadata.Files[i]
                : null;

            if (fileMetadata?.ExpandEmail is true)
            {
                byte[] emailBytes = await FormFileBytes.ReadAsync(request.Files[i], cancellationToken);
                if (_intakeGate.Reads(emailBytes, request.Files[i].FileName))
                {
                    Error? conversionError = await ExpandAndUploadEmailAsync(
                        request,
                        i,
                        emailBytes,
                        fileMetadata,
                        uploads,
                        uploadErrors,
                        intake,
                        cancellationToken);

                    if (conversionError is { } conversionFailure)
                    {
                        uploadErrors.Add(conversionFailure);
                        break;
                    }

                    continue;
                }
            }

            var uploadCommand = new UploadFileCommand(
                request.Files[i],
                request.OrganizationId,
                request.UserId,
                FilePassword: fileMetadata?.Password,
                Uploader: request.Submitter);

            ErrorOr<FileDto> uploadResult = await _sender.Send(uploadCommand, cancellationToken);

            if (uploadResult.IsError)
            {
                uploadErrors.Add(FileError(i, request.Files[i].FileName, uploadResult.FirstError));
                continue;
            }

            uploads.Add(uploadResult.Value, fileMetadata?.DocumentClass ?? string.Empty);
        }

        if (uploadErrors.Count > 0)
        {
            _logger.LogWarning(
                "Submission for workflow {WorkflowId} rejected with {FailureCount} file error(s); the response names each file and the {CreatedCount} document(s) it created are removed",
                LogSanitizer.ForLog(request.Metadata.WorkflowTypeId),
                uploadErrors.Count,
                uploads.CreatedDocumentIds.Count);
            await DeleteCreatedDocumentsAsync(request, uploads.CreatedDocumentIds, cancellationToken);
            return uploadErrors;
        }

        // 4. Delegate to orchestrator
        ErrorOr<WorkflowExecutionDto> executionResult = await _orchestrator.ExecuteWorkflowAsync(
            workflow,
            uploads.Documents,
            effectiveVariables,
            request.OrganizationId,
            request.UserId,
            request.Username,
            request.Email,
            request.Metadata.CounterpartyId,
            request.Metadata.PeerCompanyIds,
            isConfidential: false,
            idempotencyKey: ResolveIdempotencyKey(
                request.IdempotencyKey,
                request.OrganizationId,
                workflow.WorkflowTypeId,
                intake.MessageIds),
            ct: cancellationToken,
            triggerEmailIdentifier: intake.TriggerEmailIdentifier,
            intakeSkippedFiles: intake.SkippedFiles.Count > 0 ? intake.SkippedFiles : null,
            freezeSource: freezeSource);

        if (!executionResult.IsError && intake.SkippedFiles.Count > 0)
        {
            return executionResult.Value with
            {
                IntakeSkippedFiles =
                    [.. intake.SkippedFiles.Select(s => new SkippedFileDto(s.FileName, s.Reason))],
            };
        }

        return executionResult;
    }

    /// <summary>
    /// An explicit Idempotency-Key header always wins. Without one, a submission that expanded
    /// email(s) carrying Message-ID headers gets a key derived from them, so a feeder retrying
    /// the same email cannot start a second execution. Truncated to the 64-character key limit.
    /// </summary>
    private static string? ResolveIdempotencyKey(
        string? explicitKey,
        string organizationId,
        string? workflowTypeId,
        IReadOnlyList<string> expandedMessageIds)
    {
        if (!string.IsNullOrEmpty(explicitKey))
        {
            return explicitKey;
        }

        if (expandedMessageIds.Count == 0)
        {
            return null;
        }

        string material = string.Join(
            "|",
            [organizationId, workflowTypeId, .. expandedMessageIds.Order(StringComparer.Ordinal)]);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return $"email-{hash}"[..64];
    }

    /// <summary>
    /// The documents a submission attached and the subset this request created. An upload
    /// reuses a document the organization already holds under the same name and size, so a
    /// rejected submission may delete only what it created.
    /// </summary>
    private sealed class SubmissionUploads
    {
        public List<PostDocumentInfoRequest> Documents { get; } = [];

        public List<string> CreatedDocumentIds { get; } = [];

        public void Add(FileDto upload, string documentClass, bool shouldUseOcr = false)
        {
            Documents.Add(new PostDocumentInfoRequest
            {
                FileId = upload.Id,
                DocumentClass = documentClass,
                ShouldUseOcr = shouldUseOcr,
            });

            if (upload.Created)
            {
                CreatedDocumentIds.Add(upload.Id);
            }
        }
    }

    private sealed class EmailIntakeState
    {
        public string? TriggerEmailIdentifier { get; set; }

        public List<SkippedProtectedFile> SkippedFiles { get; } = [];

        public List<string> MessageIds { get; } = [];
    }

    /// <summary>
    /// Expands one email and uploads its parts. Returns the conversion error when the bytes are
    /// not an email the platform can render; every other failure is added to
    /// <paramref name="uploadErrors"/> under the email's index. A refused body ends the email's
    /// upload there, because the submission is already lost.
    /// </summary>
    private async Task<Error?> ExpandAndUploadEmailAsync(
        SubmitWorkflowExecutionCommand request,
        int index,
        byte[] emailBytes,
        SubmitFileMetadata fileMetadata,
        SubmissionUploads uploads,
        List<Error> uploadErrors,
        EmailIntakeState intake,
        CancellationToken cancellationToken)
    {
        IFormFile emailFile = request.Files[index];

        EmailExpansionResult expansion;

        try
        {
            expansion = await _emailExpansionService.ExpandAsync(emailBytes, cancellationToken);
        }
        catch (EmailConversionException ex)
        {
            _logger.LogWarning(ex, "Failed to expand an email (file name of {FileNameLength} characters).", emailFile.FileName.Length);

            return Error.Validation(
                "File.Email.ConversionFailed",
                "The email could not be opened, so it was not expanded. Save it again from the mail program, then upload the new copy.");
        }

        if (intake.TriggerEmailIdentifier is null)
        {
            intake.TriggerEmailIdentifier = expansion.TriggerEmailIdentifier;
        }
        else
        {
            _logger.LogWarning(
                "Multiple expanded emails in one submission; keeping the first trigger email identifier.");
        }

        if (expansion.MessageId is not null)
        {
            intake.MessageIds.Add(expansion.MessageId);
        }

        intake.SkippedFiles.AddRange(
            expansion.SkippedParts.Select(s => new SkippedProtectedFile(s.FileName, s.Reason)));

        string attachmentsDocumentClass = fileMetadata.AttachmentsDocumentClass ?? string.Empty;

        Error? bodyError = await UploadExpandedPartAsync(
            request,
            expansion.BodyPdf,
            fileMetadata.DocumentClass ?? string.Empty,
            shouldUseOcr: expansion.BodyShowsContentImage,
            sourceEmailIdentifier: expansion.TriggerEmailIdentifier,
            uploads,
            cancellationToken);

        if (bodyError is { } bodyFailure)
        {
            uploadErrors.Add(FileError(index, expansion.BodyPdf.FileName, bodyFailure));
            return null;
        }

        Error? rawEmailError = await UploadExpandedPartAsync(
            request,
            expansion.RawEmail,
            attachmentsDocumentClass,
            shouldUseOcr: false,
            sourceEmailIdentifier: null,
            uploads,
            cancellationToken);

        if (rawEmailError is { } rawEmailFailure)
        {
            uploadErrors.Add(FileError(index, expansion.RawEmail.FileName, rawEmailFailure));
        }

        foreach (ExpandedEmailPart part in expansion.Parts)
        {
            Error? partError = await UploadExpandedPartAsync(
                request,
                part,
                attachmentsDocumentClass,
                shouldUseOcr: false,
                sourceEmailIdentifier: expansion.TriggerEmailIdentifier,
                uploads,
                cancellationToken);

            if (partError is not { } partFailure)
            {
                continue;
            }

            if (IntakeRefusalErrors.IsRefusal(partFailure))
            {
                _logger.LogWarning(
                    "An expanded email part (file name of {FileNameLength} characters) was refused and skipped: {Reason}",
                    part.FileName.Length,
                    partFailure.Description);
                intake.SkippedFiles.Add(new SkippedProtectedFile(part.FileName, partFailure.Description));
                continue;
            }

            uploadErrors.Add(FileError(index, part.FileName, partFailure));
        }

        return null;
    }

    private static Error FileError(int index, string fileName, Error error)
    {
        return Error.Validation($"files[{index}]", $"{fileName}: {error.Description}");
    }

    private async Task DeleteCreatedDocumentsAsync(
        SubmitWorkflowExecutionCommand request,
        IReadOnlyList<string> createdDocumentIds,
        CancellationToken cancellationToken)
    {
        foreach (string documentId in createdDocumentIds)
        {
            ErrorOr<Deleted> deletion = await _sender.Send(
                new DeleteDocumentCommand(request.OrganizationId, request.UserId, documentId),
                cancellationToken);

            if (deletion.IsError)
            {
                _logger.LogWarning(
                    "Document {DocumentId} created by the rejected submission could not be removed: {Reason}",
                    documentId,
                    deletion.FirstError.Description);
            }
        }
    }

    /// <summary>
    /// Uploads one expanded part and returns the upload's error, if any. An attachment the intake
    /// gate refuses is the caller's to skip; the body and the raw email are the email itself, so
    /// their refusal is the submission's failure.
    /// </summary>
    private async Task<Error?> UploadExpandedPartAsync(
        SubmitWorkflowExecutionCommand request,
        ExpandedEmailPart part,
        string documentClass,
        bool shouldUseOcr,
        string? sourceEmailIdentifier,
        SubmissionUploads uploads,
        CancellationToken cancellationToken)
    {
        using var partStream = new MemoryStream(part.Content);

        var formFile = new FormFile(
            partStream,
            0,
            part.Content.Length,
            "file",
            part.FileName)
        {
            Headers = new HeaderDictionary(),
        };

        if (part.ContentType is not null)
        {
            formFile.ContentType = part.ContentType;
        }

        var uploadCommand = new UploadFileCommand(
            formFile,
            request.OrganizationId,
            request.UserId,
            Uploader: request.Submitter,
            SourceEmailIdentifier: sourceEmailIdentifier);

        ErrorOr<FileDto> uploadResult = await _sender.Send(uploadCommand, cancellationToken);

        if (uploadResult.IsError)
        {
            return uploadResult.FirstError;
        }

        uploads.Add(uploadResult.Value, documentClass, shouldUseOcr);

        return null;
    }

    private Dictionary<string, JsonElement> MergeVariableDefaults(
        Workflow workflow,
        Dictionary<string, JsonElement>? suppliedVariables)
    {
        // Canonicalize inbound keys to the template's PascalCase ReplacementTags first, so a
        // re-run's camelCased keys match and we neither orphan the submitted value nor create a twin.
        Dictionary<string, JsonElement> effective = VariableKeyCanonicalizer.CanonicalizeVariableKeys(
            suppliedVariables, workflow.Variables, _logger);

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            if (effective.ContainsKey(variable.ReplacementTag))
            {
                continue;
            }

            JsonElement? defaultValue = variable.DefaultValueAsJson();
            if (defaultValue is null)
            {
                continue;
            }

            effective[variable.ReplacementTag] = defaultValue.Value;
        }

        return effective;
    }

    private async Task<Workflow?> GetWorkflowAsync(string workflowId, string organizationId)
    {
        // Try by type ID first (the caller typically knows the workflow type, not the internal record ID)
        return await _coreUnitOfWork.Workflows.GetByTypeIdAsync(workflowId)
            ?? await _coreUnitOfWork.Workflows.GetByIdAsync(workflowId)
            ?? RunnableWorkflowGuard.OwnCopy(await _userDataUnitOfWork.Workflows.GetByIdAsync(workflowId), organizationId);
    }
}
