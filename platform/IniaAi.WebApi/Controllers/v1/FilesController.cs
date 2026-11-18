using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Files.GetFile;
using IniaAi.Common.Text;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Manages document upload and related document metadata.
/// </summary>
[Route("api/v{version:apiVersion}/files")]
public sealed class FilesController : ApiController
{
    private const string INLINE_DISPOSITION = "inline";

    // The API shares the web app's origin, so a file opened from it in a browser must run nothing there, embed no
    // plugin and point no relative link elsewhere.
    private const string SANDBOX_POLICY = "sandbox; object-src 'none'; base-uri 'none'";

    private readonly ILogger<FilesController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance for this controller.</param>
    /// <param name="sender">The sender for dispatching queries and commands.</param>
    public FilesController(
        IRequestContext requestContext,
        ILogger<FilesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Uploads a new file to the system.
    /// </summary>
    /// <remarks>
    /// This endpoint accepts a file via <c>multipart/form-data</c>.
    /// It validates the file size and extension, computes a unique hash for de-duplication,
    /// and stores the file associated with the current user's organization.
    /// </remarks>
    /// <param name="request">The upload request containing the file. Supported formats include .pdf, .jpg, .png, .jpeg, .csv, .xlsx, .xlsm and .docx, and in the paid build .xls, .msg and .eml.</param>
    /// <returns>The created document metadata if successful.</returns>
    /// <response code="201">File uploaded and stored successfully.</response>
    /// <response code="400">The file is empty, exceeds the allowed size, or has an unsupported format.</response>
    /// <response code="401">Authentication is required to access this endpoint.</response>
    /// <response code="403">The user does not have permission to upload files, or the organization has reached its storage limit (code <c>File.Refused.StorageFull</c>).</response>
    /// <response code="409">A file with the same content already exists (not yet enforced).</response>
    /// <response code="429">The organization has uploaded the most files allowed in an hour (code <c>File.Refused.TooManyUploads</c>). <c>Retry-After</c> gives the seconds until one more fits, and <c>resetsAt</c> the time in UTC.</response>
    /// <response code="500">An unexpected error occurred while uploading the file.</response>
    [HttpPost]
    [RequiresCapability(OrganizationCapabilities.UPLOAD)]
    [FileUploadSizeLimit]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(FileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> UploadAsync([FromForm] UploadFormRequest request)
    {
        var command = new UploadFileCommand(
            request.File,
            OrganizationId,
            UserId,
            request.DocumentCategoryId,
            request.Password,
            User);

        ErrorOr<FileDto> result = await _sender.Send(command);

        _logger.LogInformation(
            "File upload request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, FileNameLength: {FileNameLength}, Success: {Success}",
            OrganizationId,
            UserId,
            request.File.FileName.Length,
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            dto => Created($"/files/{dto.Id}", dto),
            Problem
        );
    }

    /// <summary>
    /// Downloads or previews file content by file ID.
    /// </summary>
    /// <remarks>
    /// This endpoint retrieves the binary content of a file. The <c>disposition</c> parameter
    /// controls whether the content is displayed inline (for preview) or downloaded as an attachment.
    /// Only files within the user's organization can be accessed.
    /// </remarks>
    /// <param name="id">The unique identifier of the file.</param>
    /// <param name="disposition">Content disposition: "preview" for inline display, "download" for attachment (default).</param>
    /// <returns>The file content as a file stream.</returns>
    /// <response code="200">File content returned successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="401">Authentication is required to access this endpoint.</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">File not found, it belongs to another organization, or its content is empty.</response>
    /// <response code="500">An unexpected error occurred while retrieving the file.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileStreamResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetFileAsync(
        string id,
        [FromQuery] string disposition = "download")
    {
        var query = new GetFileQuery(OrganizationId, UserId, id, disposition);
        ErrorOr<Contracts.Responses.Files.FileResult> result = await _sender.Send(query);

        _logger.LogInformation(
            "Get file request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, FileId: {FileId}, Disposition: {Disposition}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(disposition),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            content =>
            {
                if (content.ContentDisposition == INLINE_DISPOSITION)
                {
                    Response.Headers.ContentDisposition = INLINE_DISPOSITION;
                    Response.Headers.ContentSecurityPolicy = SANDBOX_POLICY;

                    return File(content.Content, content.ContentType);
                }

                return File(content.Content, content.ContentType, content.FileName);
            },
            Problem
        );
    }
}
