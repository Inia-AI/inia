using ErrorOr;
using IniaAi.Application.Commands.DocumentCategories.CreateDocumentCategory;
using IniaAi.Application.Commands.DocumentCategories.DeleteDocumentCategory;
using IniaAi.Application.Commands.DocumentCategories.UpdateDocumentCategory;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.DocumentCategories.ListDocumentCategories;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Requests.DocumentCategories;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Manages document categories.
/// </summary>
[Route("api/v{version:apiVersion}/document-categories")]
[Authorize]
public class DocumentCategoriesController : ApiController
{
    private readonly ILogger<DocumentCategoriesController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentCategoriesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="sender">The sender.</param>
    public DocumentCategoriesController(
        IRequestContext requestContext,
        ILogger<DocumentCategoriesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all document categories.
    /// </summary>
    /// <returns>A list of document categories.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DocumentCategoryDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<DocumentCategoryDto>>> GetDocumentCategories()
    {
        _logger.LogInformation("Retrieving Document Categories - OrganizationId: {OrganizationId}, UserId: {UserId}",
            OrganizationId, UserId);

        var query = new ListDocumentCategoriesQuery(OrganizationId, UserId);
        ErrorOr<List<DocumentCategoryDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<List<DocumentCategoryDto>>>(
            categories => categories,
            errors => Problem(errors)
        );
    }

    /// <summary>
    /// Creates a new document category. Categories are shared by every organization, so only an editor creates one.
    /// </summary>
    /// <param name="request">The create document category request.</param>
    /// <returns>The created document category.</returns>
    [HttpPost]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(DocumentCategoryDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentCategoryDto>> CreateDocumentCategory([FromBody] CreateDocumentCategoryRequest request)
    {
        _logger.LogInformation("Creating Document Category - Name: {Name}, OrganizationId: {OrganizationId}, UserId: {UserId}",
            LogSanitizer.ForLog(request.Name), OrganizationId, UserId);

        var command = new CreateDocumentCategoryCommand(
            OrganizationId,
            UserId,
            request.Name,
            request.DisplayOrder);

        ErrorOr<DocumentCategoryDto> result = await _sender.Send(command);

        return result.Match<ActionResult<DocumentCategoryDto>>(
            category => CreatedAtAction(nameof(GetDocumentCategories), new { id = category.Id }, category),
            errors => Problem(errors)
        );
    }

    /// <summary>
    /// Updates an existing document category. Only an editor updates one.
    /// </summary>
    /// <param name="id">The ID of the document category to update.</param>
    /// <param name="request">The update document category request.</param>
    /// <returns>The updated document category.</returns>
    [HttpPut("{id}")]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentCategoryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DocumentCategoryDto>> UpdateDocumentCategory(string id, [FromBody] UpdateDocumentCategoryRequest request)
    {
        _logger.LogInformation("Updating Document Category - Id: {Id}, OrganizationId: {OrganizationId}, UserId: {UserId}",
            LogSanitizer.ForLog(id), OrganizationId, UserId);

        var command = new UpdateDocumentCategoryCommand(
            OrganizationId,
            UserId,
            id,
            request.Name,
            request.DisplayOrder);

        ErrorOr<DocumentCategoryDto> result = await _sender.Send(command);

        return result.Match<ActionResult<DocumentCategoryDto>>(
            category => Ok(category),
            errors => Problem(errors)
        );
    }

    /// <summary>
    /// Deletes a document category. Only an editor deletes one.
    /// </summary>
    /// <param name="id">The ID of the document category to delete.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteDocumentCategory(string id)
    {
        _logger.LogInformation("Deleting Document Category - Id: {Id}, OrganizationId: {OrganizationId}, UserId: {UserId}",
            LogSanitizer.ForLog(id), OrganizationId, UserId);

        var command = new DeleteDocumentCategoryCommand(OrganizationId, UserId, id);
        ErrorOr<Deleted> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors)
        );
    }
}
