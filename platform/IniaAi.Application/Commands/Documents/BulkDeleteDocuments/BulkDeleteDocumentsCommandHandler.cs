using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Documents.BulkDeleteDocuments;

/// <summary>
/// Handler for <see cref="BulkDeleteDocumentsCommand"/>. Processes each requested
/// id in turn, collecting per-id outcomes into a
/// <see cref="BulkDeleteDocumentsResultDto"/>. DB mutations are batched and
/// committed once at the end; blob deletions are attempted after the commit so a
/// storage failure does not block the DB side of the operation. The copies made from each deleted
/// document go with it. A document that a running report reads, itself or through a copy, stays,
/// with that reason, as for one delete.
/// </summary>
public class BulkDeleteDocumentsCommandHandler
    : IRequestHandler<BulkDeleteDocumentsCommand, ErrorOr<BulkDeleteDocumentsResultDto>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly ILogger<BulkDeleteDocumentsCommandHandler> _logger;

    public BulkDeleteDocumentsCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        IFileStorageManager fileStorageManager,
        ILogger<BulkDeleteDocumentsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _fileStorageManager = fileStorageManager;
        _logger = logger;
    }

    public async Task<ErrorOr<BulkDeleteDocumentsResultDto>> Handle(
        BulkDeleteDocumentsCommand request,
        CancellationToken cancellationToken)
    {
        var result = new BulkDeleteDocumentsResultDto();
        var blobsToDelete = new HashSet<(string Provider, string Path)>();

        List<string> uniqueIds = [.. request.DocumentIds.Distinct()];

        // Load all candidates in a single query to avoid up to 500 roundtrips on the bulk path.
        IList<Document> loaded = await _unitOfWork.Documents
            .GetAllAsync(new DocumentsByIdsTrackingSpec(uniqueIds), cancellationToken);
        var documentsById = loaded.ToDictionary(d => d.Id);

        IList<Document> copies = await _unitOfWork.Documents
            .GetAllAsync(new DocumentCopiesTrackingSpec(uniqueIds, request.OrganizationId), cancellationToken);
        ILookup<string, Document> copiesByOriginal = copies.ToLookup(copy => copy.MadeFromDocumentId!);

        HashSet<string> readByUnfinishedReports = await _unitOfWork.Documents
            .GetIdsReadByUnfinishedReportsAsync(uniqueIds, request.OrganizationId, cancellationToken);

        foreach (string id in uniqueIds)
        {
            if (!documentsById.TryGetValue(id, out Document? document))
            {
                result.Failed[id] = DocumentErrorFactory.NOT_FOUND_MESSAGE;
                continue;
            }

            if (document.OrganizationId != request.OrganizationId)
            {
                _logger.LogWarning(
                    "User {UserId} in organization {OrganizationId} attempted to bulk-delete document {DocumentId} owned by {OwnerOrganizationId}",
                    request.UserId,
                    request.OrganizationId,
                    document.Id,
                    document.OrganizationId);
                result.Failed[id] = DocumentErrorFactory.NOT_FOUND_MESSAGE;
                continue;
            }

            if (request.OwnOnly && document.UserId != request.UserId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to bulk-delete document {DocumentId}, which another user uploaded",
                    request.UserId,
                    document.Id);
                result.Failed[id] = DocumentErrorFactory.NOT_FOUND_MESSAGE;
                continue;
            }

            if (readByUnfinishedReports.Contains(id))
            {
                result.Failed[id] = DocumentErrorFactory.READ_BY_RUNNING_REPORT_MESSAGE;
                continue;
            }

            foreach (Document each in copiesByOriginal[id].Prepend(document))
            {
                FileMetadata? fileMetadata = each.FileMetadata;
                if (fileMetadata is not null
                    && !string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
                {
                    _ = blobsToDelete.Add((fileMetadata.StorageProvider.ToString(), fileMetadata.ExternalStoragePath));
                }

                _unitOfWork.Documents.Remove(each);
                if (fileMetadata is not null)
                {
                    _unitOfWork.FileMetadata.Remove(fileMetadata);
                }
            }

            result.Deleted.Add(id);
        }

        if (result.Deleted.Count > 0)
        {
            _ = await _unitOfWork.CompleteAsync(cancellationToken);
        }

        foreach ((string provider, string path) in blobsToDelete)
        {
            try
            {
                await _fileStorageManager.DeleteFileAsync(
                    provider,
                    request.OrganizationId,
                    StorageCategories.Documents,
                    path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete blob at {Provider}:{Path} during bulk delete. Orphan will be reaped by the cleanup job.",
                    provider,
                    path);
            }
        }

        _logger.LogInformation(
            "User {UserId} bulk-deleted {DeletedCount} documents ({FailedCount} failed) in organization {OrganizationId}",
            request.UserId,
            result.Deleted.Count,
            result.Failed.Count,
            request.OrganizationId);

        return result;
    }
}
