using ErrorOr;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;
using IniaAi.Application.Commands.Documents.CreateTextDocument;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Commands.Documents.ProcessDocument;
using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.Queries.Documents.GetDocumentPage;
using IniaAi.Application.Queries.Documents.GetDocumentText;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.Queries.Documents.ListDocuments;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.Documents;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using System.Net.Mime;
using System.Text;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Document management endpoints. Exposes server-side pagination, filtering,
/// detail retrieval, metadata patching, and single/bulk deletion so a separate
/// frontend can implement document management without reusing the Blazor
/// WebApp code paths.
/// </summary>
[Route("api/v{version:apiVersion}/documents")]
[Authorize]
public sealed class DocumentsController : ApiController
{
    private const string PLAIN_TEXT_UTF8 = $"{MediaTypeNames.Text.Plain}; charset=utf-8";

    private readonly ISender _sender;

    /// <summary>Initializes a new instance of the <see cref="DocumentsController"/> class.</summary>
    /// <param name="requestContext">The request context supplying organization and user identifiers.</param>
    /// <param name="sender">The sender used to dispatch queries and commands.</param>
    public DocumentsController(
        IRequestContext requestContext,
        ISender sender)
        : base(requestContext)
    {
        _sender = sender;
    }

    /// <summary>
    /// Retrieves a paginated, OData-filterable list of documents scoped to the
    /// caller's organization. Supports <c>$filter</c>, <c>$orderby</c>,
    /// <c>$top</c>, <c>$skip</c>, and <c>$count=true</c>. An optional
    /// <paramref name="jobId"/> narrows the result to documents processed under a
    /// specific workflow job (matches <c>DocumentInfo.JobId</c>).
    /// </summary>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<DocumentSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<DocumentSummaryDto>>> ListDocuments(
        ODataQueryOptions<DocumentSummaryDto> queryOptions,
        [FromQuery] string? jobId = null)
    {
        var query = new PaginatedResponseDocumentsQuery(OrganizationId, UserId, queryOptions, jobId);
        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<DocumentSummaryDto>>>(
            page => page,
            errors => Problem(errors));
    }

    /// <summary>
    /// Retrieves the detail projection for a single document.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentDetailDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDocument(string id)
    {
        var query = new GetDocumentQuery(OrganizationId, UserId, id);
        ErrorOr<DocumentDetailDto> result = await _sender.Send(query);
        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Applies a partial update to a document. Fields left null (or whitespace)
    /// are ignored; omitted fields preserve their stored value. <c>isStatic</c> false
    /// clears the workflow type and class that make a document static.
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateDocument(string id, [FromBody] UpdateDocumentRequest request)
    {
        var command = new UpdateDocumentCommand(
            OrganizationId,
            UserId,
            id,
            request.Name,
            request.Description,
            request.DocumentCategoryId,
            request.WorkflowTypeId,
            request.Class,
            request.Topics,
            request.TableOfContents,
            request.IsStatic);

        ErrorOr<DocumentDetailDto> result = await _sender.Send(command);
        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Deletes a single document, its file metadata row, and the backing blob
    /// from storage. Blob deletion failures are logged but do not roll back the
    /// database delete; the cleanup job handles orphan blobs.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.ADMIN_AND_OPERATOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteDocument(string id)
    {
        var command = new DeleteDocumentCommand(OrganizationId, UserId, id);
        ErrorOr<Deleted> result = await _sender.Send(command);
        return result.Match<IActionResult>(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Deletes many documents in a single call. Each id is processed
    /// independently; the response returns the deleted ids and a per-id failure
    /// map for ids that could not be removed.
    /// </summary>
    [HttpPost("bulk-delete")]
    [Authorize(Roles = Roles.ADMIN_AND_OPERATOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BulkDeleteDocumentsResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BulkDeleteDocuments([FromBody] BulkDeleteDocumentsRequest request)
    {
        var command = new BulkDeleteDocumentsCommand(OrganizationId, UserId, request.Ids);
        ErrorOr<BulkDeleteDocumentsResultDto> result = await _sender.Send(command);
        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves a paginated, OData-filterable list of pages for a document.
    /// The parent document's organization is checked before any pages are
    /// returned. The <c>term</c> query parameter keeps the page with that number and
    /// the pages whose full text or overview contains it, ignoring case. A plain
    /// <c>search</c> would be read by OData as its own <c>$search</c>.
    /// </summary>
    [HttpGet("{id}/pages")]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<PageSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<PageSummaryDto>>> ListDocumentPages(
        string id,
        ODataQueryOptions<PageSummaryDto> queryOptions,
        [FromQuery(Name = PaginatedResponseDocumentPagesQuery.SEARCH_PARAMETER)] string? search = null)
    {
        var query = new PaginatedResponseDocumentPagesQuery(OrganizationId, UserId, id, queryOptions, search);
        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<PageSummaryDto>>>(
            page => page,
            errors => Problem(errors));
    }

    /// <summary>
    /// Reads one page of a document with its full text — what an extraction reads for that page.
    /// </summary>
    /// <response code="200">The page.</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">Document or page not found, or the document belongs to another organization.</response>
    [HttpGet("{id}/pages/{pageNumber:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PageDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentPage(string id, int pageNumber)
    {
        ErrorOr<PageDetailDto> result = await _sender.Send(new GetDocumentPageQuery(OrganizationId, UserId, id, pageNumber));

        return result.Match(Ok, Problem);
    }

    /// <summary>
    /// Downloads the text of every page of a document as one plain-text file, in page order, each
    /// page wrapped in a <c>&lt;pageN&gt;</c> tag.
    /// </summary>
    /// <response code="200">The text file.</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">Document not found, or it belongs to another organization.</response>
    [HttpGet("{id}/text")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocumentText(string id)
    {
        ErrorOr<DocumentText> result = await _sender.Send(new GetDocumentTextQuery(OrganizationId, UserId, id));

        return result.Match<IActionResult>(
            text => File(Encoding.UTF8.GetBytes(text.Text), PLAIN_TEXT_UTF8, text.FileName),
            Problem);
    }

    /// <summary>
    /// Queues a stored document for standalone processing into pages: PDF, images, spreadsheets,
    /// CSV, .docx, plain text and markdown. Emails (.eml/.msg) are stored but never paged and are
    /// refused here. Poll <c>GET /documents/{id}</c> — <c>processingState</c> moves from
    /// <c>Queued</c> to <c>Processed</c> or <c>Failed</c> (with <c>processingError</c>).
    /// </summary>
    /// <response code="202">Processing queued; the body is the document as it stands.</response>
    /// <response code="400">The document's format is never processed into pages.</response>
    /// <response code="403">The request names no organization the caller belongs to, or the organization cannot upload.</response>
    /// <response code="404">Document not found, or it belongs to another organization.</response>
    /// <response code="409">Processing is already queued for this document.</response>
    [HttpPost("{id}/process")]
    [RequiresCapability(OrganizationCapabilities.STANDALONE_PROCESSING)]
    [RequiresCapability(OrganizationCapabilities.UPLOAD)]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(DocumentSummaryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcessDocument(string id)
    {
        ErrorOr<DocumentSummaryDto> result = await _sender.Send(new ProcessDocumentCommand(OrganizationId, UserId, id));

        return result.Match(
            queued => AcceptedAtAction(nameof(GetDocument), new { id = queued.Id, version = "1.0" }, queued),
            Problem);
    }

    /// <summary>
    /// Creates a document from pasted text or markdown and queues it for processing: the whole
    /// text becomes page 1, with no format extraction in between. Poll <c>GET /documents/{id}</c>
    /// for <c>processingState</c>.
    /// </summary>
    /// <response code="202">Document created and processing queued.</response>
    /// <response code="400">Validation errors (empty name or text, text over the limit).</response>
    /// <response code="403">The organization cannot upload, or it has reached its storage limit (code <c>File.Refused.StorageFull</c>).</response>
    /// <response code="429">The organization has uploaded the most files allowed in an hour (code <c>File.Refused.TooManyUploads</c>). <c>Retry-After</c> gives the seconds until one more fits.</response>
    [HttpPost("text")]
    [RequiresCapability(OrganizationCapabilities.STANDALONE_PROCESSING)]
    [RequiresCapability(OrganizationCapabilities.UPLOAD)]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(DocumentSummaryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateTextDocument([FromBody] CreateTextDocumentRequest request)
    {
        ErrorOr<DocumentSummaryDto> result = await _sender.Send(
            new CreateTextDocumentCommand(OrganizationId, UserId, request.Name, request.Text, request.Format, User));

        return result.Match(
            queued => AcceptedAtAction(nameof(GetDocument), new { id = queued.Id, version = "1.0" }, queued),
            Problem);
    }
}
