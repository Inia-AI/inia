using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Reports;
using IniaAi.Application.Errors;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Files.GetFile;

/// <summary>
/// Handles <see cref="GetFileQuery"/> to stream a file's content from its store.
/// </summary>
public sealed class GetFileQueryHandler : IRequestHandler<GetFileQuery, ErrorOr<FileResult>>
{
    private readonly ILogger<GetFileQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IReportContentRule _reportContent;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetFileQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for operational diagnostics.</param>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <param name="fileStorageManager">The file storage manager for retrieving file content.</param>
    /// <param name="reportContent">Decides whether the report that produced a file may be read.</param>
    public GetFileQueryHandler(
        ILogger<GetFileQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IFileStorageManager fileStorageManager,
        IReportContentRule reportContent)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _fileStorageManager = fileStorageManager;
        _reportContent = reportContent;
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

        if (await RefusalOfProducingReportAsync(document, request.OrganizationId, cancellationToken) is { } refusal)
        {
            return Error.Forbidden(description: refusal);
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

        string fileName = document.MediaType.ServedFileName(fileMetadata.Name.TrimEnd().WithExtension(fileMetadata.Extension));
        string contentType = document.MediaType.ToContentType();

        Stream content;
        try
        {
            content = await _fileStorageManager.RetrieveFileAsync(
                fileMetadata.StorageProvider.ToString(),
                fileMetadata.OrganizationId,
                StorageCategories.Documents,
                fileMetadata.ExternalStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving content for document {FileId}",
                LogSanitizer.ForLog(request.FileId));
            return Error.Failure(description: "Error retrieving document content.");
        }

        long? length = StoredObjectStream.LengthOf(content);
        if (length == 0)
        {
            await content.DisposeAsync();
            _logger.LogWarning(
                "File content is empty for document {FileId}",
                LogSanitizer.ForLog(request.FileId));
            return Error.NotFound(description: "File content is empty.");
        }

        string contentDisposition = request.Disposition.Equals("preview", StringComparison.OrdinalIgnoreCase)
            ? "inline"
            : "attachment";

        _logger.LogTrace(
            "Successfully retrieved content for document {FileId}, size: {Size} bytes",
            LogSanitizer.ForLog(request.FileId),
            length);

        return new FileResult
        {
            Content = content,
            Length = length,
            ContentType = contentType,
            FileName = fileName,
            ContentDisposition = contentDisposition,
        };
    }

    /// <summary>
    /// A file a report produced is part of that report, so it is refused when the report's content is. An upload is the
    /// customer's own and always comes back. A produced file names its report by the report's job.
    /// </summary>
    private async Task<string?> RefusalOfProducingReportAsync(Document document, string organizationId, CancellationToken cancellationToken)
    {
        if (document.UserId is not null)
        {
            return null;
        }

        List<string> reportIds = await (
            from info in _userDataUnitOfWork.Repository<DocumentInfo>().GetAll()
            join run in _userDataUnitOfWork.Repository<WorkflowExecution>().GetAll() on info.JobId equals run.JobId
            where info.DocumentId == document.Id && run.OrganizationId == organizationId
            select run.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (string reportId in reportIds)
        {
            if (await _reportContent.RefusalAsync(organizationId, reportId, cancellationToken) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }
}
