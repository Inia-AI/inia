using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApp.Attributes;
using IniaAi.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IniaAi.WebApp.Controllers;

[ApiController]
[Route("[controller]")]
[RoleOrMessageProcessorApiKeyAuthorize]
public class FileDownloadController : ControllerBase
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ILogger<FileDownloadController> _logger;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly PlatformSettings? _platformSettings;
    private readonly CallerOrganizations _callerOrganizations;

    public FileDownloadController(
        IUserDataUnitOfWork userDataUnitOfWork,
        ILogger<FileDownloadController> logger,
        IOptions<PlatformSettings> options,
        IFileStorageManager fileStorageManager,
        CallerOrganizations callerOrganizations)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _logger = logger;
        _platformSettings = options?.Value;
        _fileStorageManager = fileStorageManager;
        _callerOrganizations = callerOrganizations;
    }

    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> DownloadDocument(string documentId)
    {
        try
        {
            Document? document = await _userDataUnitOfWork.Documents.GetByIdAsync(documentId);
            IReadOnlyList<string>? readable = await _callerOrganizations.ReadableAsync(HttpContext);

            if (document is null || (readable is not null && !readable.Contains(document.OrganizationId)))
            {
                return NotFound("Document not found.");
            }

            FileMetadata? fileMetadata = await _userDataUnitOfWork.FileMetadata.GetByIdAsync(document.FileMetadataId);
            if (fileMetadata is null)
            {
                return NotFound("File metadata not found.");
            }

            string fileName = fileMetadata.Name ?? $"document_{documentId}";

            if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
            {
                return NotFound("File not found in storage.");
            }

            string downloadName = document.MediaType.ServedFileName(fileName.TrimEnd().WithExtension(fileMetadata.Extension));

            return await StreamAsync(fileMetadata, document.MediaType.ToContentType(), downloadName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading document {DocumentId}", LogSanitizer.ForLog(documentId));
            return StatusCode(500, "Error downloading document.");
        }
    }

    [HttpGet("file/{fileName}")]
    public async Task<IActionResult> DownloadFileByName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("File name is required.");
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return BadRequest("Invalid file name.");
        }

        if (!Path.HasExtension(fileName))
        {
            return BadRequest("File name must include an extension.");
        }

        try
        {
            string input = fileName.Trim();
            IReadOnlyList<string>? readable = await _callerOrganizations.ReadableAsync(HttpContext);
            FileMetadata? fileMetadata = await FileMetadataByName.NewestAsync(_userDataUnitOfWork, fileName, readable);

            if (fileMetadata is null)
            {
                return NotFound("File not found.");
            }

            if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
            {
                return NotFound("File not found in storage.");
            }

            string downloadName =
                $"{Path.GetFileNameWithoutExtension(fileMetadata.Name) ?? input}{fileMetadata.Extension}";

            string contentType = await ContentTypeOfAsync(fileMetadata);

            return await StreamAsync(fileMetadata, contentType, downloadName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error downloading a file by name (name of {FileNameLength} characters)",
                fileName.Length);
            return StatusCode(500, "Error downloading file.");
        }
    }

    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        bool apiKeyConfigured = !string.IsNullOrEmpty(_platformSettings?.MessageProcessor?.ApiKey);
        string authDescription = apiKeyConfigured
            ? "Authenticated user or API Key (X-API-Key)"
            : "Authenticated user required";

        var info = new
        {
            name = "File Download Controller",
            description = "Downloads files by document ID or by file name with content type inference.",
            endpoints = new object[]
            {
                new
                {
                    method = "GET",
                    path = "/FileDownload/info",
                    description = "Returns information about this controller and its usage"
                },
                new
                {
                    method = "GET",
                    path = "/FileDownload/document/{documentId}",
                    description = "Downloads a file by the associated document ID",
                    authentication = authDescription,
                    parameters = new
                    {
                        documentId = new
                        {
                            type = "string",
                            required = true,
                            description = "Identifier of the document whose file to download"
                        }
                    },
                    responses = new
                    {
                        success = new { statusCode = 200, content = "application/octet-stream (file bytes)" },
                        notFound = new { statusCode = 404, examples = new object[] { new { error = "Document not found." }, new { error = "File not found." }, new { error = "File content is empty." } } },
                        serverError = new { statusCode = 500, example = new { error = "Error downloading document." } }
                    }
                },
                new
                {
                    method = "GET",
                    path = "/FileDownload/file/{fileName}",
                    description = "Downloads a file by its full name (case-insensitive exact match)",
                    authentication = authDescription,
                    parameters = new
                    {
                        fileName = new
                        {
                            type = "string",
                            required = true,
                            description = "Full file name including extension; slashes are not allowed (e.g., 'report.pdf')"
                        }
                    },
                    responses = new
                    {
                        success = new { statusCode = 200, content = "inferred from extension; defaults to application/octet-stream" },
                        badRequest = new { statusCode = 400, examples = new object[] { new { error = "File name is required." }, new { error = "Invalid file name." }, new { error = "File name must include an extension." } } },
                        notFound = new { statusCode = 404, examples = new object[] { new { error = "File not found." }, new { error = "File content is empty." } } },
                        serverError = new { statusCode = 500, example = new { error = "Error downloading file." } }
                    }
                }
            },
            usage = new
            {
                curlExamples = apiKeyConfigured
                    ? new object[]
                    {
                        $"curl -X GET https://yourserver/FileDownload/document/{{documentId}} -H \"X-API-Key: your-api-key\" -OJ",
                        $"curl -X GET https://yourserver/FileDownload/file/{{fileName}} -H \"X-API-Key: your-api-key\" -OJ"
                    }
                    : [
                        "curl -X GET https://yourserver/FileDownload/document/{documentId} -H \"Authorization: Bearer <token>\" -OJ",
                        "curl -X GET https://yourserver/FileDownload/file/{fileName} -H \"Authorization: Bearer <token>\" -OJ"
                    ],
                notes = new string[]
                {
                    apiKeyConfigured ? "API Key authentication supported via X-API-Key header; authenticated users are also allowed" : "Authenticated users only; API key not configured",
                    "File name lookups require an extension and are case-insensitive exact matches; the newest file wins when names collide",
                    "Content type is the media type detected when the file was stored; a file without a document falls back to its extension"
                }
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        return new JsonResult(info, options);
    }

    /// <summary>
    /// Streams the stored file to the answer, with its size when the store gives it. An empty file
    /// answers as not found, as before.
    /// </summary>
    private async Task<IActionResult> StreamAsync(FileMetadata fileMetadata, string contentType, string downloadName)
    {
        Stream content = await _fileStorageManager.RetrieveFileAsync(
            fileMetadata.StorageProvider.ToString(),
            fileMetadata.OrganizationId,
            StorageCategories.Documents,
            fileMetadata.ExternalStoragePath!);
        long? length = StoredObjectStream.LengthOf(content);
        if (length == 0)
        {
            await content.DisposeAsync();
            return NotFound("File content is empty.");
        }

        Response.ContentLength = length;
        return File(content, contentType, downloadName);
    }

    private async Task<string> ContentTypeOfAsync(FileMetadata fileMetadata)
    {
        if (fileMetadata.DocumentId is not null)
        {
            fileMetadata.Document = await _userDataUnitOfWork.Documents.GetByIdAsync(fileMetadata.DocumentId);
        }

        return fileMetadata.ContentType;
    }
}
