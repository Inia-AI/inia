using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Files;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Files.GetFile;

/// <summary>
/// Handles <see cref="GetFileQuery"/> to retrieve file binary content.
/// </summary>
public sealed class GetFileQueryHandler : IRequestHandler<GetFileQuery, ErrorOr<FileResult>>
{
    private readonly ILogger<GetFileQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IFileStorageManager _fileStorageManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetFileQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for operational diagnostics.</param>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <param name="fileStorageManager">The file storage manager for retrieving file content.</param>
    public GetFileQueryHandler(
        ILogger<GetFileQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IFileStorageManager fileStorageManager)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _fileStorageManager = fileStorageManager;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<FileResult>> Handle(
        GetFileQuery request,
        CancellationToken cancellationToken)
    {
        Document? document = await _userDataUnitOfWork.Documents.GetByIdAsync(
            request.FileId,
            cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "Document with ID: {FileId} not found",
                LogSanitizer.ForLog(request.FileId));
            return DocumentErrorFactory.NotFound();
        }

        if (document.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User attempted to access document {FileId} outside their organization",
                LogSanitizer.ForLog(request.FileId));
            return DocumentErrorFactory.NotFound();
        }

        FileMetadata? fileMetadata = await _userDataUnitOfWork
            .FileMetadata
            .GetByIdAsync(document.FileMetadataId, cancellationToken);

        if (fileMetadata is null)
        {
            _logger.LogWarning(
                "FileMetadata for document {FileId} not found",
                LogSanitizer.ForLog(request.FileId));
            return Error.NotFound(description: "File metadata not found.");
        }

        if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
        {
            _logger.LogWarning(
                "ExternalStoragePath is missing for document {FileId}",
                LogSanitizer.ForLog(request.FileId));
            return Error.NotFound(description: "File storage path not found.");
        }

        try
        {
            byte[] fileBytes = await _fileStorageManager.RetrieveFileBytesAsync(
                fileMetadata.StorageProvider.ToString(),
                fileMetadata.OrganizationId,
                StorageCategories.Documents,
                fileMetadata.ExternalStoragePath);

            if (fileBytes is null || fileBytes.Length == 0)
            {
                _logger.LogWarning(
                    "File content is empty for document {FileId}",
                    LogSanitizer.ForLog(request.FileId));
                return Error.NotFound(description: "File content is empty.");
            }

            string fileName = fileMetadata.Name.TrimEnd().WithExtension(fileMetadata.Extension);
            string contentType = document.MediaType.ToContentType();

            string contentDisposition = request.Disposition.Equals("preview", StringComparison.OrdinalIgnoreCase)
                ? "inline"
                : "attachment";

            var result = new FileResult
            {
                Content = fileBytes,
                ContentType = contentType,
                FileName = fileName,
                ContentDisposition = contentDisposition,
            };

            _logger.LogTrace(
                "Successfully retrieved content for document {FileId}, size: {Size} bytes",
                LogSanitizer.ForLog(request.FileId),
                fileBytes.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving content for document {FileId}",
                LogSanitizer.ForLog(request.FileId));
            return Error.Failure(description: "Error retrieving document content.");
        }
    }
}
