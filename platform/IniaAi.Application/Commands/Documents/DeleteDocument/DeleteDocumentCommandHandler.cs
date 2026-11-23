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
/// FileMetadata in a single DB transaction, then attempts to delete each blob
/// from external storage. Blob deletion failures are logged but do not roll back
/// the DB removal — an orphan blob is preferable to an undeletable document. A failed delete is
/// recorded for a retry, and the daily sweep removes any file no row points at. The copies made from the
/// document go with it. A document that a running report reads, itself or through a copy, is refused,
/// so the report never loses its input.
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

        if (request.OwnOnly && document.UserId != request.UserId)
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete document {DocumentId}, which another user uploaded",
                request.UserId,
                document.Id);
            return DocumentErrorFactory.NotFound();
        }

        HashSet<string> readByUnfinishedReports = await _unitOfWork.Documents
            .GetIdsReadByUnfinishedReportsAsync([document.Id], request.OrganizationId, cancellationToken);
        if (readByUnfinishedReports.Count > 0)
        {
            return DocumentErrorFactory.ReadByRunningReport();
        }

        IList<Document> copies = await _unitOfWork.Documents
            .GetAllAsync(new DocumentCopiesTrackingSpec([document.Id], request.OrganizationId), cancellationToken);
        List<Document> removed = [document, .. copies];

        foreach (Document each in removed)
        {
            _unitOfWork.Documents.Remove(each);
            if (each.FileMetadata is not null)
            {
                _unitOfWork.FileMetadata.Remove(each.FileMetadata);
            }
        }

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        foreach (Document each in removed)
        {
            await DeleteBlobAsync(each, request.OrganizationId);
        }

        _logger.LogInformation(
            "User {UserId} deleted document {DocumentId} in organization {OrganizationId}",
            request.UserId,
            document.Id,
            request.OrganizationId);

        return Result.Deleted;
    }

    private async Task DeleteBlobAsync(Document document, string organizationId)
    {
        string? storageProvider = document.FileMetadata?.StorageProvider.ToString();
        string? storagePath = document.FileMetadata?.ExternalStoragePath;
        if (string.IsNullOrEmpty(storageProvider) || string.IsNullOrEmpty(storagePath))
        {
            return;
        }

        if (await _fileStorageManager.ReleaseFileAsync(storageProvider, organizationId, StorageCategories.Documents, storagePath) is { } ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete blob for document {DocumentId} at {StorageProvider}:{StoragePath}. Orphan will be reaped by the cleanup job.",
                document.Id,
                storageProvider,
                storagePath);
        }
    }
}
