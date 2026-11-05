using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Pages.GetPageImage;
using IniaAi.Common.Text;
using IniaAi.WebApi.Contracts.Responses.Pages;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Manages page-related operations including image rendering.
/// </summary>
[Route("api/v{version:apiVersion}/pages")]
public sealed class PagesController : ApiController
{
    private readonly ILogger<PagesController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="PagesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance for this controller.</param>
    /// <param name="sender">The sender for dispatching queries and commands.</param>
    public PagesController(
        IRequestContext requestContext,
        ILogger<PagesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Gets the image representation of a document page.
    /// </summary>
    /// <remarks>
    /// Returns the page as an image:
    /// - PDF pages are rendered to PNG with automatic cropping for split layouts
    /// - Image files (PNG, JPEG, etc.) are returned as-is
    /// - Other files (Word, Excel, etc.) return the page text as text/plain
    ///
    /// Supports HTTP caching with ETag and Cache-Control headers.
    /// </remarks>
    /// <param name="pageId">The unique identifier of the page.</param>
    /// <returns>The page image or text content.</returns>
    /// <response code="200">Page image returned successfully.</response>
    /// <response code="304">Not modified - client cache is still valid.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">Page, document, or file not found, or the page belongs to another organization.</response>
    /// <response code="500">Error generating page image.</response>
    [HttpGet("{pageId}/image")]
    [Produces("image/png", "image/jpeg", "text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetPageImageAsync(string pageId)
    {
        var query = new GetPageImageQuery(OrganizationId, UserId, pageId);
        ErrorOr<PageImageResultDto> result = await _sender.Send(query);

        _logger.LogInformation(
            "Get page image request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, PageId: {PageId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(pageId),
            result.IsError ? "No" : "Yes");

        return result.Match(HandleSuccessResult, Problem);
    }

    private IActionResult HandleSuccessResult(PageImageResultDto content)
    {
        // Check If-None-Match header for conditional GET
        if (!string.IsNullOrEmpty(content.ETag))
        {
            string? requestETag = Request.Headers.IfNoneMatch.FirstOrDefault();

            if (requestETag == content.ETag)
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        // Set caching headers
        Response.Headers[HeaderNames.CacheControl] = "private, max-age=3600";

        if (!string.IsNullOrEmpty(content.ETag))
        {
            Response.Headers[HeaderNames.ETag] = content.ETag;
        }

        if (content.LastModified.HasValue)
        {
            Response.Headers[HeaderNames.LastModified] = content.LastModified.Value.ToString("R");
        }

        return File(content.Content, content.ContentType);
    }
}
