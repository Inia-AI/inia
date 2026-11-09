using ErrorOr;
using IniaAi.Application.Commands.Signatures;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Documents;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Unlock;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace IniaAi.Application.Commands.Files.UploadFile;

/// <summary>
/// Handles processing and storage of a file upload. The bytes pass <see cref="DocumentIntakeGate"/>
/// first: a refusal answers with the reason code and leaves no row and no blob, and an admitted
/// file is stored as the type the inspector detected, whatever its name says. A file the
/// organization already holds is reused at no cost; a new one must fit the organization's
/// <see cref="IUploadLimits"/>, which let it through when they cannot be read.
/// </summary>
public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, ErrorOr<FileDto>>
{
    private const string PASSWORD_REQUIRED_CODE = "File.Password.Required";
    private const string PASSWORD_INCORRECT_CODE = "File.Password.Incorrect";
    private const string PASSWORD_UNSUPPORTED_CODE = "File.Password.Unsupported";

    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<UploadFileCommandHandler> _logger;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly DocumentIntakeGate _intakeGate;
    private readonly IUploadLimits _uploadLimits;
    private readonly IDocumentPageCounter _pageCounter;
    private readonly IEmailExpansionService _emailExpansion;

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadFileCommandHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The user data unit of work.</param>
    /// <param name="logger">Logger for tracing.</param>
    /// <param name="fileStorageManager">File storage manager for handling file persistence.</param>
    /// <param name="intakeGate">The gate every upload passes before its bytes are stored.</param>
    /// <param name="uploadLimits">The organization's storage quota and upload rate.</param>
    /// <param name="pageCounter">Counts the pages of the stored file.</param>
    /// <param name="emailExpansion">Counts the pages of an email's body and of its attachments, apart.</param>
    public UploadFileCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<UploadFileCommandHandler> logger,
        IFileStorageManager fileStorageManager,
        DocumentIntakeGate intakeGate,
        IUploadLimits uploadLimits,
        IDocumentPageCounter pageCounter,
        IEmailExpansionService emailExpansion)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _fileStorageManager = fileStorageManager;
        _intakeGate = intakeGate;
        _uploadLimits = uploadLimits;
        _pageCounter = pageCounter;
        _emailExpansion = emailExpansion;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<FileDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting file processing for user {UserId} in organization {OrgId}.",
            request.UserId,
            request.OrganizationId);

        string storageFileName = request.File.FileName;
        var mouth = new IntakeMouth(_unitOfWork, request.OrganizationId);

        if (await RefuseEarlyAsync(request, mouth, cancellationToken) is { } earlyRefusal)
        {
            return earlyRefusal;
        }

        IntakeScreening screening = await _intakeGate.ScreenAsync(
            request.File,
            request.FilePassword,
            mouth,
            FileContentInspector.Default,
            cancellationToken);

        if (screening.Outcome is not IntakeOutcome.Admitted)
        {
            return Reject(screening, storageFileName, request.OrganizationId);
        }

        byte[] fileBytes = screening.Bytes;
        FileUnlockResult? unlockResult = screening.Unlock;
        if (unlockResult is { Status: FileProtectionStatus.Decrypted })
        {
            _logger.LogInformation(
                "Removed password protection at upload ({DecryptedSize} bytes, file name of {FileNameLength} characters).",
                storageFileName.Length,
                fileBytes.Length);
        }

        string hash = CalculateMD5(fileBytes);

        _logger.LogInformation(
            "File copied to memory and hashed. FileNameLength: {FileNameLength}, Hash: {Hash}, Size: {Size} bytes, Type: {MediaType}.",
            storageFileName.Length,
            hash,
            fileBytes.Length,
            screening.ContentType);

        Document? document = null;
        bool created = false;
        try
        {
            document = await _unitOfWork.Documents.FindExistingDocumentAsync(storageFileName, fileBytes.Length, request.OrganizationId);
            UploadCount count = document is { CountedPages: not null }
                ? new UploadCount(document.CountedPages, document.CountedAttachmentPages, document.EmailIdentifier)
                : await CountAsync(fileBytes, screening.MediaType, storageFileName, cancellationToken);
            string? storagePath = null;

            async Task PersistAsync(CancellationToken ct)
            {
                if (document is null)
                {
                    document = await _unitOfWork.Documents.UploadNewFileCreateNewDocumentAsync(
                        fileBytes,
                        request.OrganizationId,
                        request.UserId,
                        storageFileName,
                        _fileStorageManager.GetProviderForTenant(request.OrganizationId),
                        screening.MediaType);
                    created = true;
                }

                if (document.Sources.Count == 0)
                {
                    document.Sources = ["User Upload"];
                }

                if (request.SourceEmailIdentifier is { } email && !document.Sources.Contains(email))
                {
                    document.Sources = [.. document.Sources, email];
                }

                if (!string.IsNullOrWhiteSpace(request.DocumentCategoryId))
                {
                    document.DocumentCategoryId = request.DocumentCategoryId;
                }

                if (screening.HasMacros)
                {
                    document.HasMacros = true;
                }

                document.CountedPages = count.Pages;
                document.CountedAttachmentPages = count.AttachmentPages;
                document.EmailIdentifier = count.EmailIdentifier;

                if (unlockResult is { WasPasswordProtected: true })
                {
                    document.WasPasswordProtected = true;
                    await WriteUnlockAuditAsync(document, unlockResult, request, ct);
                }

                storagePath = await DocumentStorage.StoreBytesAsync(_fileStorageManager, document, fileBytes);
                _ = await _unitOfWork.CompleteAsync(ct);
            }

            if (document is not null)
            {
                await PersistAsync(cancellationToken);
            }
            else if (await StoreWithinLimitsAsync(request, fileBytes.Length, PersistAsync, cancellationToken) is { } refusal)
            {
                await _intakeGate.RecordRefusalAsync(
                    IntakeRefusal.OfBytes(storageFileName, fileBytes, refusal.ReasonCode, screening.ContentType),
                    mouth,
                    cancellationToken);
                return refusal.ToError();
            }

            document = document ?? throw new InvalidOperationException("The upload stored no document.");

            if (storagePath is not null)
            {
                _logger.LogInformation(
                    "File stored successfully. FileId: {FileId}, Provider: {Provider}, StoragePath: {StoragePath}.",
                    document.FileMetadata?.Id,
                    document.FileMetadata?.StorageProvider,
                    storagePath);
            }

            _logger.LogInformation(
                "File persisted successfully. DocumentId: {DocumentId}, Hash: {Hash}.",
                document.Id,
                hash);

            FileDto fileDto = FileDtoMapper.ToDto(document);
            fileDto.Created = created;

            if (screening.MediaType == MediaType.ApplicationPdf && !PdfUtils.HasValidStructure(fileBytes))
            {
                _logger.LogWarning(
                    "An uploaded PDF (file name of {FileNameLength} characters) appears to be incomplete or damaged (missing startxref/%%EOF).",
                    storageFileName.Length);

                fileDto.Warnings ??= [];
                fileDto.Warnings.Add(
                    "This PDF looks damaged. The platform repairs it while reading it, but some content may be missing. "
                    + "If the results look wrong, download it again from its source.");
            }

            if (unlockResult is { ContainedSignature: true })
            {
                fileDto.Warnings ??= [];
                fileDto.Warnings.Add(
                    "This document contained a digital signature. Removing the password protection "
                    + "invalidates the signature; the document content is unchanged.");
            }

            return fileDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to upload a file (name of {FileNameLength} characters) for user {UserId} in organization {OrgId}.",
                request.File.FileName.Length,
                request.UserId,
                request.OrganizationId);

            if (document is not null && created)
            {
                _unitOfWork.Documents.Remove(document);
                _ = await _unitOfWork.CompleteAsync(cancellationToken);
            }

            return Error.Failure(
                "File.Upload.Failed",
                "The file could not be uploaded. Try again.");
        }
    }

    /// <summary>
    /// Refuses before the bytes are read when the organization has no more uploads this hour. A file
    /// the organization already holds, by name and declared size, costs nothing and is never refused.
    /// </summary>
    private async Task<Error?> RefuseEarlyAsync(UploadFileCommand request, IntakeMouth mouth, CancellationToken cancellationToken)
    {
        UploadRefusal? refusal;
        try
        {
            if (await _unitOfWork.Documents.FindExistingDocumentAsync(request.File.FileName, request.File.Length, request.OrganizationId) is not null)
            {
                return null;
            }

            refusal = await _uploadLimits.CheckRateAsync(LimitsRequest(request, request.File.Length), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUnreadableLimits(ex, request);
            return null;
        }

        if (refusal is null)
        {
            return null;
        }

        await _intakeGate.RecordRefusalAsync(IntakeRefusal.Unread(request.File.FileName, request.File.Length, refusal.ReasonCode), mouth, cancellationToken);
        return refusal.ToError();
    }

    /// <summary>
    /// Stores a new file within the organization's limits. When the limits cannot be read, the file
    /// is stored without them; a failure of the store itself goes to the caller.
    /// </summary>
    private async Task<UploadRefusal?> StoreWithinLimitsAsync(
        UploadFileCommand request,
        long sizeBytes,
        Func<CancellationToken, Task> persist,
        CancellationToken cancellationToken)
    {
        bool persisting = false;
        try
        {
            return await _uploadLimits.StoreWithinLimitsAsync(
                LimitsRequest(request, sizeBytes),
                ct =>
                {
                    persisting = true;
                    return persist(ct);
                },
                cancellationToken);
        }
        catch (Exception ex) when (!persisting && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            LogUnreadableLimits(ex, request);
            await persist(cancellationToken);
            return null;
        }
    }

    private static UploadLimitsRequest LimitsRequest(UploadFileCommand request, long sizeBytes)
    {
        return new UploadLimitsRequest(request.OrganizationId, request.Uploader, sizeBytes);
    }

    private void LogUnreadableLimits(Exception ex, UploadFileCommand request)
    {
        _logger.LogWarning(ex, "The upload limits of organization {OrgId} could not be read; the upload goes through", request.OrganizationId);
    }

    private async Task WriteUnlockAuditAsync(
        Document document,
        FileUnlockResult unlockResult,
        UploadFileCommand request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string?> actorEmails = await SignatureActorResolver.ResolveEmailsAsync(
            _unitOfWork,
            [request.UserId],
            cancellationToken);

        // Add, not AddAsync: AddAsync saves at once, before this document's bytes are stored.
        _unitOfWork.Repository<DocumentUnlockAuditLog>().Add(DocumentUnlockAuditLog.For(
            document,
            unlockResult.ContainedSignature,
            passwordSupplied: !string.IsNullOrEmpty(request.FilePassword),
            actorUserId: request.UserId,
            actorUserEmail: actorEmails.GetValueOrDefault(request.UserId)));

        _logger.LogInformation(
            "Password protection removed for document {DocumentId} by user {UserId}; unlock audit entry written.",
            document.Id,
            request.UserId);
    }

    private Error Reject(IntakeScreening screening, string fileName, string organizationId)
    {
        switch (screening.Outcome)
        {
            case IntakeOutcome.Refused:
                _logger.LogWarning(
                    "Refused an upload (file name of {FileNameLength} characters) for organization {OrgId}: {ReasonCode}.",
                    fileName.Length,
                    organizationId,
                    screening.ReasonCode);
                return IntakeRefusalErrors.Refused(screening);
            case IntakeOutcome.PasswordRequired:
                return Error.Validation(
                    PASSWORD_REQUIRED_CODE,
                    "The file is password-protected. Provide the file password to upload it.");
            case IntakeOutcome.PasswordIncorrect:
                return Error.Validation(
                    PASSWORD_INCORRECT_CODE,
                    "The supplied password does not open this file. Check the password and try again.");
            case IntakeOutcome.UnsupportedProtection:
                return Error.Validation(
                    PASSWORD_UNSUPPORTED_CODE,
                    "The file is protected in a way that a password cannot remove, such as a certificate. "
                    + "Remove the protection in the program that made it, then upload it again.");
            case IntakeOutcome.Admitted:
            default:
                throw new ArgumentOutOfRangeException(nameof(screening), screening.Outcome, "Only a non-admitted screening is rejected.");
        }
    }

    private async Task<UploadCount> CountAsync(byte[] bytes, MediaType mediaType, string fileName, CancellationToken cancellationToken)
    {
        if (!mediaType.IsEmail())
        {
            return new UploadCount(_pageCounter.Count(bytes, mediaType, fileName), null, null);
        }

        try
        {
            EmailPageCount email = await _emailExpansion.CountPagesAsync(bytes, cancellationToken);
            return new UploadCount(email.BodyPages, email.AttachmentPages, email.Identifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "An uploaded email (file name of {FileNameLength} characters) could not be read, so its pages were not counted.", fileName.Length);
            return new UploadCount(null, null, null);
        }
    }

    /// <summary>
    /// Calculates the MD5 hash of the uploaded file bytes.
    /// </summary>
    /// <param name="bytes">File content as byte array.</param>
    /// <returns>Hexadecimal MD5 hash string.</returns>
    private static string CalculateMD5(byte[] bytes)
    {
        byte[] hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed record UploadCount(int? Pages, int? AttachmentPages, string? EmailIdentifier);
}
