using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Documents.DeleteDocument;

/// <summary>
/// Handler for <see cref="DeleteDocumentCommand"/>. Removes the document and its
/// FileMetadata in a single DB transaction, then attempts to delete the blob
/// from external storage. Blob deletion failures are logged but do not roll back
/// the DB removal — an orphan blob is preferable to an undeletable document, and
/// existing cleanup jobs (<c>SoftDeleteCleanupService</c>) handle orphans.
/// </summary>
public class DeleteDocumentCommandHandler : IRequestHandler<DeleteDocumentCommand, ErrorOr<Deleted>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly ILogger<DeleteDocumentCommandHandler> _logger;

    public DeleteDocumentCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        IFileStorageManager fileStorageManager,
        ILogger<DeleteDocumentCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _fileStorageManager = fileStorageManager;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        Document? document = await _unitOfWork.Documents
            .GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(request.DocumentId), cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete non-existent document {DocumentId}",
                request.UserId,
                LogSanitizer.ForLog(request.DocumentId));
            return DocumentErrorFactory.NotFound();
        }

        if (document.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to delete document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                document.Id,
                document.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        FileMetadata? fileMetadata = document.FileMetadata;
        string? storageProvider = fileMetadata?.StorageProvider.ToString();
        string? storagePath = fileMetadata?.ExternalStoragePath;

        _unitOfWork.Documents.Remove(document);
        if (fileMetadata is not null)
        {
            _unitOfWork.FileMetadata.Remove(fileMetadata);
        }

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        if (!string.IsNullOrEmpty(storageProvider) && !string.IsNullOrEmpty(storagePath))
        {
            try
            {
                await _fileStorageManager.DeleteFileAsync(
                    storageProvider,
                    request.OrganizationId,
                    StorageCategories.Documents,
                    storagePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete blob for document {DocumentId} at {StorageProvider}:{StoragePath}. Orphan will be reaped by the cleanup job.",
                    document.Id,
                    storageProvider,
                    storagePath);
            }
        }

        _logger.LogInformation(
            "User {UserId} deleted document {DocumentId} in organization {OrganizationId}",
            request.UserId,
            document.Id,
            request.OrganizationId);

        return Result.Deleted;
    }
}
