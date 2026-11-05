using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Constants;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Email;
using IniaAi.FileFormats.Unlock;
using IniaAi.Services.Models.Jobs;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Jobs;

/// <summary>
/// Turns the raw email documents of a job into the documents the workflow expects — the body
/// rendered to PDF and, when requested, every extracted attachment — before document processing
/// runs. Acts only on jobs whose inputs carry <see cref="IntakeInputKeys.EMAIL_INTAKE_MODE"/>;
/// consumes the intake keys so reruns and downstream code never see them. The raw email document
/// itself is left in place as the audit artifact. Minted documents carry the trigger email
/// identifier in <c>Document.Sources</c>, and an email whose identifier already appears among the
/// job's document sources is skipped — so replays and re-runs that carry the previously minted
/// products do not expand the same email twice. Two further nets close the gaps that marker
/// cannot see: a minted product that resolves to a document the job already references is not
/// linked a second time, and the identifier is appended to a re-minted document's existing
/// sources — so products created before the marker existed heal on their first re-run.
/// Every minted part passes <see cref="DocumentIntakeGate"/> before its bytes are stored: a refused
/// attachment is skipped with its reason and its refusal record joins the run's trail, and a refused
/// body stops the email like a failed render would.
/// </summary>
public class EmailIntakeJobStage
{
    private readonly IEmailExpansionService _emailExpansionService;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly DocumentIntakeGate _intakeGate;
    private readonly ILogger<EmailIntakeJobStage> _logger;

    public EmailIntakeJobStage(
        IEmailExpansionService emailExpansionService,
        IFileStorageManager fileStorageManager,
        IWorkflowOutputService workflowOutputService,
        DocumentIntakeGate intakeGate,
        ILogger<EmailIntakeJobStage> logger)
    {
        _emailExpansionService = emailExpansionService;
        _fileStorageManager = fileStorageManager;
        _workflowOutputService = workflowOutputService;
        _intakeGate = intakeGate;
        _logger = logger;
    }

    public async Task ProcessAsync(
        Job job,
        string? workflowExecutionId,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        if (!job.Inputs.TryGetValue(IntakeInputKeys.EMAIL_INTAKE_MODE, out string? mode))
        {
            return;
        }

        _ = job.Inputs.Remove(IntakeInputKeys.EMAIL_INTAKE_MODE);

        string attachmentsDocumentClass = job.Inputs.TryGetValue(
            IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS, out string? configuredClass)
            ? configuredClass
            : string.Empty;
        _ = job.Inputs.Remove(IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS);

        List<DocumentInfo> documentInfos = await userDataUnitOfWork.Documents
            .GetByJobIdWithMetadataAsync(job.Id);

        var outcome = new EmailIntakeOutcome();
        await ExpandEmailsAsync(
            new EmailIntakeTarget(job.OrganizationId, job.UserId, job.InputIsVouchedFor, job.Id),
            documentInfos,
            documentInfos,
            mode,
            attachmentsDocumentClass,
            workflowExecutionId,
            userDataUnitOfWork,
            outcome,
            cancellationToken);

        if (!outcome.HadEmails)
        {
            return;
        }

        job.DocumentInfos.AddRange(outcome.Products.Select(product => product.Document.Id));

        foreach (string triggerEmailIdentifier in outcome.TriggerEmailIdentifiers)
        {
            AddTriggerEmailIdentifierInput(job, triggerEmailIdentifier);
        }

        MergeSkippedFiles(job, outcome.SkippedFiles);

        _ = await userDataUnitOfWork.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Expands every email among <paramref name="emails"/> into the documents the target receives,
    /// without committing, and records them in <paramref name="outcome"/> as it goes — so when a
    /// later email fails, what the earlier ones yielded is still recorded. <paramref name="documentInfos"/>
    /// are all the target's documents, so a minted product that is already among them is not linked
    /// twice. The editor's test runs call this with the emails they have not expanded yet, so an
    /// uploaded email reaches a block the way it reaches a report.
    /// </summary>
    public async Task ExpandEmailsAsync(
        EmailIntakeTarget target,
        IReadOnlyList<DocumentInfo> documentInfos,
        IEnumerable<DocumentInfo> emails,
        string mode,
        string attachmentsDocumentClass,
        string? workflowExecutionId,
        IUserDataUnitOfWork userDataUnitOfWork,
        EmailIntakeOutcome outcome,
        CancellationToken cancellationToken)
    {
        List<(DocumentInfo Info, Document Document)> pendingIntake = [.. emails
            .Where(di => di.Document is not null && di.Document.MediaType.IsEmail())
            .Select(di => (Info: di, Document: di.Document!))];

        outcome.HadEmails = pendingIntake.Count > 0;

        if (pendingIntake.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Email intake stage: processing {IntakeCount} email(s) in mode {Mode} for {Run}",
            pendingIntake.Count,
            mode,
            target.RunLabel);

        bool extractAttachments = mode == EmailIntakeModes.EXTRACT_ATTACHMENTS;

        HashSet<string> jobDocumentSources = [.. documentInfos
            .Where(di => di.Document is not null)
            .SelectMany(di => di.Document!.Sources)];

        HashSet<string> jobDocumentIds = [.. documentInfos.Select(di => di.DocumentId)];

        foreach ((DocumentInfo sourceInfo, Document sourceDocument) in pendingIntake)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessEmailAsync(
                target,
                workflowExecutionId,
                userDataUnitOfWork,
                sourceInfo,
                sourceDocument,
                jobDocumentSources,
                jobDocumentIds,
                extractAttachments,
                attachmentsDocumentClass,
                outcome,
                cancellationToken);
        }
    }

    private async Task ProcessEmailAsync(
        EmailIntakeTarget target,
        string? workflowExecutionId,
        IUserDataUnitOfWork userDataUnitOfWork,
        DocumentInfo sourceInfo,
        Document sourceDocument,
        HashSet<string> jobDocumentSources,
        HashSet<string> jobDocumentIds,
        bool extractAttachments,
        string attachmentsDocumentClass,
        EmailIntakeOutcome outcome,
        CancellationToken cancellationToken)
    {
        FileMetadata? fileMetadata = sourceDocument.FileMetadata;
        string fileName = fileMetadata is null
            ? sourceDocument.Name
            : fileMetadata.Name + fileMetadata.Extension;

        try
        {
            if (fileMetadata?.ExternalStoragePath is null)
            {
                throw new InvalidOperationException(
                    $"Email document '{fileName}' has no stored file content to process.");
            }

            byte[] emailBytes = await _fileStorageManager.RetrieveFileBytesAsync(
                fileMetadata.StorageProvider.ToString(),
                target.OrganizationId,
                StorageCategories.Documents,
                fileMetadata.ExternalStoragePath);

            string triggerEmailIdentifier = await _emailExpansionService.DeriveTriggerEmailIdentifierAsync(
                emailBytes, cancellationToken);

            if (jobDocumentSources.Contains(triggerEmailIdentifier))
            {
                _logger.LogInformation(
                    "Email intake stage: {Run} already contains the intake products of document {DocumentId}; skipping re-expansion",
                    target.RunLabel,
                    sourceInfo.DocumentId);

                outcome.TriggerEmailIdentifiers.Add(triggerEmailIdentifier);
                outcome.HandledEmailIds.Add(sourceInfo.Id);
                return;
            }

            ExpandedEmailPart bodyPdf;
            bool bodyShowsContentImage;
            IReadOnlyList<ExpandedEmailPart> extractedParts = [];

            if (extractAttachments)
            {
                EmailExpansionResult expansion = await _emailExpansionService.ExpandAsync(
                    emailBytes, cancellationToken);

                bodyPdf = expansion.BodyPdf;
                bodyShowsContentImage = expansion.BodyShowsContentImage;
                extractedParts = expansion.Parts;

                outcome.SkippedFiles.AddRange(
                    expansion.SkippedParts.Select(s => new SkippedProtectedFile(s.FileName, s.Reason)));
            }
            else
            {
                EmailBodyRenderResult render = await _emailExpansionService.RenderBodyAsync(
                    emailBytes, cancellationToken);

                bodyPdf = render.BodyPdf;
                bodyShowsContentImage = render.BodyShowsContentImage;
            }

            var mouth = new IntakeMouth(userDataUnitOfWork, target.OrganizationId, workflowExecutionId, target.InputIsVouchedFor);

            string? bodyRefusal = await MintDocumentAsync(
                target, userDataUnitOfWork, mouth, bodyPdf, triggerEmailIdentifier, jobDocumentIds,
                sourceInfo.DocumentClass, shouldUseOcr: bodyShowsContentImage, sourceInfo.Id, outcome, cancellationToken);

            if (bodyRefusal is not null)
            {
                throw new InvalidOperationException(
                    $"The rendered body '{bodyPdf.FileName}' was not admitted: {bodyRefusal}");
            }

            foreach (ExpandedEmailPart part in extractedParts)
            {
                string? skipReason = await MintDocumentAsync(
                    target, userDataUnitOfWork, mouth, part, triggerEmailIdentifier, jobDocumentIds,
                    attachmentsDocumentClass, shouldUseOcr: false, sourceInfo.Id, outcome, cancellationToken);

                if (skipReason is null)
                {
                    continue;
                }

                _logger.LogWarning(
                    "Email intake stage skipped an attachment (file name of {FileNameLength} characters) for {Run}: {Reason}",
                    part.FileName.Length,
                    target.RunLabel,
                    skipReason);
                outcome.SkippedFiles.Add(new SkippedProtectedFile(part.FileName, skipReason));
            }

            _ = jobDocumentSources.Add(triggerEmailIdentifier);
            outcome.TriggerEmailIdentifiers.Add(triggerEmailIdentifier);
            outcome.HandledEmailIds.Add(sourceInfo.Id);
        }
        catch (Exception ex) when (ex is EmailConversionException or InvalidOperationException)
        {
            _logger.LogError(ex, "Email intake failed for document {DocumentId} in {Run}", sourceInfo.DocumentId, target.RunLabel);

            if (target.JobId is not null)
            {
                await _workflowOutputService.AppendDocumentIntakeErrorLogAsync(
                    workflowExecutionId,
                    fileName,
                    $"The email could not be processed: {ex.Message}");
            }

            throw new InvalidOperationException(
                $"Email '{fileName}' could not be converted to PDF; the execution cannot proceed without its body.",
                ex);
        }
    }

    private void AddTriggerEmailIdentifierInput(Job job, string triggerEmailIdentifier)
    {
        if (!job.Inputs.TryAdd(SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER, triggerEmailIdentifier))
        {
            _logger.LogWarning("Multiple emails in one job; keeping the first trigger email identifier.");
        }
    }

    /// <summary>
    /// Screens and stores one expanded part. Returns null when the part became a document, or
    /// the operator-facing reason it did not; the caller decides whether that reason is a skip
    /// or the end of the email.
    /// </summary>
    private async Task<string?> MintDocumentAsync(
        EmailIntakeTarget target,
        IUserDataUnitOfWork userDataUnitOfWork,
        IntakeMouth mouth,
        ExpandedEmailPart part,
        string triggerEmailIdentifier,
        HashSet<string> jobDocumentIds,
        string documentClass,
        bool shouldUseOcr,
        string sourceEmailId,
        EmailIntakeOutcome outcome,
        CancellationToken cancellationToken)
    {
        byte[] unlockedContent = part.Unlock?.DecryptedBytes ?? part.Content;
        IntakeScreening screening = await _intakeGate.ScreenAsync(
            unlockedContent, part.FileName, password: null, mouth, cancellationToken);

        if (IntakeSkipReasons.NonInteractive(screening) is { } skipReason)
        {
            return skipReason;
        }

        byte[] content = screening.Bytes;
        FileUnlockResult? unlock = screening.Unlock ?? part.Unlock;

        (Document document, _) = await userDataUnitOfWork.Documents.GetOrCreateDocumentAsync(
            content,
            target.OrganizationId,
            target.UserId,
            part.FileName,
            content.Length,
            _fileStorageManager.GetProviderForTenant(target.OrganizationId),
            screening.MediaType);

        if (!document.Sources.Contains(triggerEmailIdentifier))
        {
            document.Sources = [.. document.Sources, triggerEmailIdentifier];
        }

        if (!jobDocumentIds.Add(document.Id))
        {
            _logger.LogInformation(
                "Email intake stage: {Run} already references document {DocumentId}; not linking it twice",
                target.RunLabel,
                document.Id);
            return null;
        }

        if (screening.HasMacros)
        {
            document.HasMacros = true;
        }

        if (unlock is { WasPasswordProtected: true })
        {
            document.WasPasswordProtected = true;

            // Add, not AddAsync: AddAsync saves at once, before this document's bytes are stored.
            userDataUnitOfWork.Repository<DocumentUnlockAuditLog>().Add(DocumentUnlockAuditLog.For(
                document,
                unlock.ContainedSignature,
                passwordSupplied: false,
                actorUserId: target.UserId,
                actorUserEmail: null));
        }

        _ = await DocumentStorage.StoreBytesAsync(_fileStorageManager, document, content);

        DocumentInfo documentInfo = await userDataUnitOfWork.Documents.CreateDocumentInfoAsync(document);
        documentInfo.JobId = target.JobId;
        documentInfo.DocumentClass = documentClass;
        documentInfo.ShouldUseOcr = shouldUseOcr;

        outcome.Products.Add(new EmailIntakeProduct(sourceEmailId, documentInfo));

        _logger.LogInformation(
            "Email intake stage minted document {DocumentId} (class '{DocumentClass}') for {Run}",
            document.Id,
            documentClass,
            target.RunLabel);

        return null;
    }

    private static void MergeSkippedFiles(Job job, IReadOnlyList<SkippedProtectedFile> skippedFiles)
    {
        if (skippedFiles.Count == 0)
        {
            return;
        }

        List<SkippedProtectedFile> merged = job.Inputs.TryGetValue(
            IntakeInputKeys.SKIPPED_PROTECTED_FILES, out string? existing)
            ? SkippedProtectedFile.Deserialize(existing)
            : [];

        merged.AddRange(skippedFiles);

        job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES] = SkippedProtectedFile.Serialize(merged);
    }
}
