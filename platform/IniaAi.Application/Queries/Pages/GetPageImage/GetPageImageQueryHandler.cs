using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Pdf;
using IniaAi.WebApi.Contracts.Responses.Pages;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Application.Queries.Pages.GetPageImage;

/// <summary>
/// Handles <see cref="GetPageImageQuery"/> to retrieve a page image.
/// </summary>
public sealed class GetPageImageQueryHandler : IRequestHandler<GetPageImageQuery, ErrorOr<PageImageResultDto>>
{
    private const int PNG_RENDER_DPI = 100;

    private static readonly Error _pageNotFound = Error.NotFound(description: "Page not found.");

    private readonly ILogger<GetPageImageQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IPdfFormat _pdfFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPageImageQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">A logger instance.</param>
    /// <param name="userDataUnitOfWork">IUserDataUnitOfWork for data access.</param>
    /// <param name="fileStorageManager">IFileStorageManager for file retrieval.</param>
    /// <param name="pdfFormat">Renders a PDF page.</param>
    public GetPageImageQueryHandler(
        ILogger<GetPageImageQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IFileStorageManager fileStorageManager,
        IPdfFormat pdfFormat)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _fileStorageManager = fileStorageManager;
        _pdfFormat = pdfFormat;
    }

    /// <summary>
    /// Handles the <see cref="GetPageImageQuery"/> to retrieve a page image.
    /// </summary>
    /// <param name="request">The query request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>>An <see cref="ErrorOr{T}"/> containing the page image result or an error.</returns>
    public async Task<ErrorOr<PageImageResultDto>> Handle(
        GetPageImageQuery request,
        CancellationToken cancellationToken)
    {
        Page? page = await _userDataUnitOfWork.Repository<Page>()
            .SingleOrDefaultAsync(
                p => p.Id == request.PageId,
                includeProperties: "Document,Document.FileMetadata",
                asNoTracking: true,
                cancellationToken);

        if (page is null)
        {
            _logger.LogWarning("Page with ID: {PageId} not found", LogSanitizer.ForLog(request.PageId));
            return _pageNotFound;
        }

        Document? document = page.Document;

        if (document is null)
        {
            _logger.LogWarning("Document for page {PageId} not found", LogSanitizer.ForLog(request.PageId));
            Document? deletedHere = await _userDataUnitOfWork.Documents
                .GetFirstOrDefaultAsync(new DeletedDocumentInOrganizationSpec(page.DocumentId, request.OrganizationId), cancellationToken);
            return deletedHere is null ? _pageNotFound : DocumentErrorFactory.Deleted();
        }

        if (document.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User attempted to access page {PageId} outside their organization",
                LogSanitizer.ForLog(request.PageId));
            return _pageNotFound;
        }

        FileMetadata? fileMetadata = document.FileMetadata;
        if (fileMetadata is null)
        {
            _logger.LogWarning("FileMetadata for document {DocumentId} not found", document.Id);
            return Error.NotFound(description: "File metadata not found.");
        }

        if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
        {
            _logger.LogWarning(
                "ExternalStoragePath is missing for document {DocumentId}",
                document.Id);
            return Error.NotFound(description: "File storage path not found.");
        }

        try
        {
            return await GeneratePageImageAsync(page, document, fileMetadata, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image for page {PageId}", LogSanitizer.ForLog(request.PageId));
            return Error.Failure(description: "Error generating page image.");
        }
    }

    private async Task<PageImageResultDto> GeneratePageImageAsync(
        Page page,
        Document document,
        FileMetadata fileMetadata,
        CancellationToken cancellationToken)
    {
        return IsImageMediaType(document.MediaType)
            ? await GetOriginalImageAsync(fileMetadata)
            : document.MediaType is MediaType.ApplicationPdf ? await RenderPdfPageAsync(page, document.Name, fileMetadata) : GetPageTextResult(page);
    }

    private async Task<PageImageResultDto> GetOriginalImageAsync(FileMetadata fileMetadata)
    {
        byte[] fileBytes = await _fileStorageManager.RetrieveFileBytesAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath!);

        string etag = GenerateETag(fileBytes);

        return new PageImageResultDto
        {
            Content = fileBytes,
            ContentType = fileMetadata.ContentType,
            ETag = etag,
            LastModified = fileMetadata.UpdatedAt ?? fileMetadata.CreatedAt,
        };
    }

    private async Task<PageImageResultDto> RenderPdfPageAsync(Page page, string documentName, FileMetadata fileMetadata)
    {
        byte[] pdfBytes = await _fileStorageManager.RetrieveFileBytesAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath!);

        using IPdfDocument pdf = _pdfFormat.Open(new MemoryStream(pdfBytes), documentName);

        PdfBox? area = page.Location switch
        {
            PageLocation.LeftSide => pdf.Box(page.FilePageNumber).Half(isLeftSide: true),
            PageLocation.RightSide => pdf.Box(page.FilePageNumber).Half(isLeftSide: false),
            _ => null,
        };

        BinaryContent imageContent = pdf.RenderPng(page.FilePageNumber, PNG_RENDER_DPI, area);

        byte[] imageBytes = imageContent.BinaryData.ToArray();
        string etag = GenerateETag(imageBytes);

        return new PageImageResultDto
        {
            Content = imageBytes,
            ContentType = "image/png",
            ETag = etag,
            LastModified = fileMetadata.UpdatedAt ?? fileMetadata.CreatedAt,
        };
    }

    private static PageImageResultDto GetPageTextResult(Page page)
    {
        string text = page.Text ?? string.Empty;
        byte[] textBytes = Encoding.UTF8.GetBytes(text);
        string etag = GenerateETag(textBytes);

        return new PageImageResultDto
        {
            Content = textBytes,
            ContentType = "text/plain; charset=utf-8",
            ETag = etag,
            LastModified = null,
        };
    }

    private static bool IsImageMediaType(MediaType mediaType)
    {
        return mediaType is MediaType.ImagePng
            or MediaType.ImageJpeg
            or MediaType.ImageGif
            or MediaType.ImageWebp
            or MediaType.ImageBmp
            or MediaType.ImageTiff;
    }

    private static string GenerateETag(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        return $"\"{Convert.ToBase64String(hash)[..22]}\"";
    }
}
