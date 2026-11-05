using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.CreateSharedComponent;
using IniaAi.Application.Commands.SharedComponents.DeleteSharedComponentCategory;
using IniaAi.Application.Commands.SharedComponents.RestoreSharedComponentVersion;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Commands.SharedComponents.SaveSharedComponentCategory;
using IniaAi.Application.Commands.SharedComponents.SaveSharedComponentContent;
using IniaAi.Application.Commands.SharedComponents.SetSharedComponentCategory;
using IniaAi.Application.Commands.SharedComponents.UnretireSharedComponent;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponent;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumers;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentContent;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersionContent;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersions;
using IniaAi.Application.Queries.SharedComponents.ListSharedComponentCategories;
using IniaAi.Application.Queries.SharedComponents.ListSharedComponents;
using IniaAi.Application.Queries.SharedComponents.PreviewSharedComponentReconcile;
using IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Requests.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// The shared-component Library registry: component and version reads, plus the curated category
/// vocabulary and re-categorization writes. The registry is one global pool per deployment:
/// endpoints are gated by editor role, not by organization access.
/// </summary>
[Route("api/v{version:apiVersion}/shared-components")]
public sealed class SharedComponentsController : ApiController
{
    private const string EDITOR_ROLES = Roles.EDITOR_ROLES;

    private readonly ILogger<SharedComponentsController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedComponentsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public SharedComponentsController(
        IRequestContext requestContext,
        ILogger<SharedComponentsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Lists all shared components with head version tokens and consumer counts.
    /// </summary>
    /// <param name="contentHash">Optional exact-match filter on the head content hash (64 lowercase hex characters).</param>
    /// <param name="categoryId">Optional exact-match filter on the curated category id; uncategorized components never match.</param>
    /// <param name="workflowTypeId">Optional filter keeping only components with a live linked copy in that workflow type.</param>
    /// <param name="workflowTypeCategoryId">Optional filter keeping only components consumed by workflow types filed under that workflow-type category.</param>
    /// <returns>All registry entries, ordered by name.</returns>
    /// <response code="200">Shared components listed successfully.</response>
    /// <response code="400">The content hash filter is malformed.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SharedComponentSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListSharedComponents(
        [FromQuery] string? contentHash = null,
        [FromQuery] string? categoryId = null,
        [FromQuery] string? workflowTypeId = null,
        [FromQuery] string? workflowTypeCategoryId = null)
    {
        ErrorOr<List<SharedComponentSummaryDto>> result = await _sender.Send(
            new ListSharedComponentsQuery(
                OrganizationId,
                UserId,
                contentHash,
                CategoryId: categoryId,
                WorkflowTypeId: workflowTypeId,
                WorkflowTypeCategoryId: workflowTypeCategoryId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Finds template content that repeats verbatim across the deployment without being in the
    /// Library yet.
    /// </summary>
    /// <remarks>
    /// The bootstrap path for a deployment whose templates were hand-copied before shared components
    /// existed. Matching is exact normalized-hash only — near-duplicates are never suggested. A group
    /// is reported when the content occurs in two or more places, or when it already matches an
    /// active component's head (<c>existingComponentId</c>, meaning attach rather than create).
    /// Candidates are nodes that are not actively Linked; a broken-out copy counts and discloses the
    /// component it came from. Blocks inside an actively Linked section belong to that section's
    /// component and are never independent candidates.
    ///
    /// Nothing is written. This reads every workflow template's full graph, so it is a deliberate
    /// on-demand sweep rather than something to poll — <c>scannedWorkflowTypeCount</c> and
    /// <c>elapsedMilliseconds</c> report what it cost. Act on a group with
    /// <c>POST /workflow-templates/{workflowTypeId}/shared-components/promote</c>: attach every node
    /// when <c>existingComponentId</c> is set, otherwise promote one node and attach the rest to the
    /// component id it returns.
    /// </remarks>
    /// <returns>Every repeated-content group with the places it occurs.</returns>
    /// <response code="200">Scan completed successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("suggestions")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentSuggestionsResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ScanSuggestions()
    {
        ErrorOr<SharedComponentBootstrapScanResult> result = await _sender.Send(
            new ScanSharedComponentBootstrapQuery(OrganizationId, UserId));

        _logger.LogInformation(
            "ScanSuggestions - Result: {Result}",
            LogSanitizer.ForLog(result.IsError
                ? $"Error - {result.FirstError.Description}"
                : $"{result.Value.Suggestions.Count} group(s) across {result.Value.ScannedWorkflowTypeCount} type(s)"));

        return result.Match<IActionResult>(
            scan => Ok(SharedComponentResponseMapper.ToResponse(scan)),
            Problem);
    }

    /// <summary>
    /// Lists the Library's curated category vocabulary with per-category component counts.
    /// </summary>
    /// <remarks>
    /// Categories are the flat browse spine of the Library. The response also carries the count of
    /// components that predate the catalogue and carry no category yet.
    /// </remarks>
    /// <returns>The vocabulary in display order, plus the uncategorized count.</returns>
    /// <response code="200">Categories listed successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("categories")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentCategoriesDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListSharedComponentCategories()
    {
        ErrorOr<SharedComponentCategoriesDto> result = await _sender.Send(
            new ListSharedComponentCategoriesQuery(OrganizationId, UserId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Creates or updates one entry of the Library's curated category vocabulary.
    /// </summary>
    /// <param name="categoryId">The category id — stable kebab-case (lowercase letters, digits, single dashes).</param>
    /// <param name="request">The category's display fields.</param>
    /// <returns>The saved vocabulary entry with its component count (retired included).</returns>
    /// <response code="200">Category saved successfully.</response>
    /// <response code="400">The id or a field is malformed.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPut("categories/{categoryId}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentCategoryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveSharedComponentCategory(
        string categoryId,
        [FromBody] SaveSharedComponentCategoryRequest request)
    {
        ErrorOr<SharedComponentCategoryDto> result = await _sender.Send(
            new SaveSharedComponentCategoryCommand(
                OrganizationId,
                UserId,
                categoryId,
                request.Name,
                request.Description,
                request.DisplayOrder));

        _logger.LogInformation(
            "SaveSharedComponentCategory {CategoryId} - {Outcome}",
            LogSanitizer.ForLog(categoryId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Deletes one entry of the Library's curated category vocabulary.
    /// </summary>
    /// <param name="categoryId">The category id.</param>
    /// <response code="204">Category deleted successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No category with this id exists.</response>
    /// <response code="409">The category is still referenced by components; re-categorize them first.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("categories/{categoryId}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteSharedComponentCategory(string categoryId)
    {
        ErrorOr<Deleted> result = await _sender.Send(
            new DeleteSharedComponentCategoryCommand(OrganizationId, UserId, categoryId));

        _logger.LogInformation(
            "DeleteSharedComponentCategory {CategoryId} - {Outcome}",
            LogSanitizer.ForLog(categoryId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Re-files a shared component under a different curated category.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="request">The target category.</param>
    /// <returns>The component's updated registry entry.</returns>
    /// <response code="200">Component re-categorized successfully.</response>
    /// <response code="400">The request is malformed.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such component, or the target category does not exist.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPut("{sharedComponentId}/category")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentSummaryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SetSharedComponentCategory(
        string sharedComponentId,
        [FromBody] SetSharedComponentCategoryRequest request)
    {
        ErrorOr<SharedComponentSummaryDto> result = await _sender.Send(
            new SetSharedComponentCategoryCommand(OrganizationId, UserId, sharedComponentId, request.CategoryId));

        _logger.LogInformation(
            "SetSharedComponentCategory for {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves a single shared component's registry entry.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <returns>The registry entry with head version tokens and consumer count.</returns>
    /// <response code="200">Shared component retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentSummaryDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponent(string sharedComponentId)
    {
        ErrorOr<SharedComponentSummaryDto> result = await _sender.Send(
            new GetSharedComponentQuery(OrganizationId, UserId, sharedComponentId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Lists the workflow types holding live linked copies of a shared component.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <remarks>
    /// The where-used surface: one row per consuming template, with its live linked-copy count and
    /// the workflow type's category. Broken-out copies never count — they stopped following the
    /// head. An unconsumed component returns an empty list.
    /// </remarks>
    /// <returns>The consuming workflow types.</returns>
    /// <response code="200">Consumers listed successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}/consumers")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ConsumingWorkflowTypeDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponentConsumers(string sharedComponentId)
    {
        ErrorOr<List<SharedComponentConsumer>> result = await _sender.Send(
            new GetSharedComponentConsumersQuery(OrganizationId, UserId, sharedComponentId));

        return result.Match<IActionResult>(
            consumers => Ok(SharedComponentResponseMapper.ToResponse(consumers)),
            Problem);
    }

    /// <summary>
    /// Returns the consumers×entries grid for a section component.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <remarks>
    /// Columns are the head definition's entries, in definition order, followed by any slot a
    /// consumer filled locally (<c>isLocalOnly</c>). Rows are the live linked copies — one row per
    /// copy, so a workflow type holding two copies appears twice, distinguished by
    /// <c>stableSectionId</c>. Each cell reads <c>Live</c>, <c>Custom</c> (that entry broke out),
    /// <c>Hidden</c>, <c>Added</c> (a locally inserted block), <c>Missing</c> (the definition owns
    /// the entry but the copy has no block for it), or <c>NotApplicable</c> (a local-only column
    /// another copy introduced). Block components have no entries and return empty lists.
    /// </remarks>
    /// <returns>The grid.</returns>
    /// <response code="200">Grid returned successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists, or its head payload is not stored.</response>
    /// <response code="409">The component's chain has no versions at all.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}/consumer-matrix")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentConsumerMatrixResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponentConsumerMatrix(string sharedComponentId)
    {
        ErrorOr<SharedComponentConsumerMatrix> result = await _sender.Send(
            new GetSharedComponentConsumerMatrixQuery(OrganizationId, UserId, sharedComponentId));

        return result.Match<IActionResult>(
            matrix => Ok(SharedComponentResponseMapper.ToResponse(matrix)),
            Problem);
    }

    /// <summary>
    /// Retrieves the definition content of a shared component's head version.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <remarks>
    /// Returns the verbatim definition JSON plus the head version tokens. The definition is the
    /// component's shared content only — per-consumer fields (a block's tag and order; a
    /// section's order) are zeroed, and instance link fields never appear. Document-class wiring
    /// is shared content and stays in the definition.
    /// </remarks>
    /// <returns>The definition content with head version tokens.</returns>
    /// <response code="200">Definition content retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such component, or its head version has no stored content.</response>
    /// <response code="409">The component's chain has no versions at all.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentContentResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponentContent(string sharedComponentId)
    {
        ErrorOr<SharedComponentContentResult> result = await _sender.Send(
            new GetSharedComponentContentQuery(OrganizationId, UserId, sharedComponentId));

        _logger.LogInformation(
            "GetSharedComponentContent for {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            content => Ok(ToContentResponse(content)),
            Problem);
    }

    /// <summary>
    /// Lists a shared component's version history, newest first.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="includeNarratives">When true, each entry also carries a human-readable summary of what changed versus its parent.</param>
    /// <remarks>
    /// Lineage is expressed through content hashes and parent version pointers. Entries flagged
    /// <c>payloadAbsent</c> are hash-only lineage rows without stored content. Narratives are
    /// computed from stored payloads, so entries adjacent to a payload-absent row carry none.
    /// </remarks>
    /// <returns>The version history.</returns>
    /// <response code="200">Version history retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}/versions")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SharedComponentVersionDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponentVersions(
        string sharedComponentId,
        [FromQuery] bool includeNarratives = false)
    {
        ErrorOr<List<SharedComponentVersionDto>> result = await _sender.Send(
            new GetSharedComponentVersionsQuery(OrganizationId, UserId, sharedComponentId, includeNarratives));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves the definition content of one stored version of a shared component.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="versionId">The version whose content to serve.</param>
    /// <remarks>
    /// The head-content contract extended to any chain row: the stored definition JSON is served
    /// verbatim with the version's identifying tokens. Hash-only lineage rows have no stored
    /// content to serve.
    /// </remarks>
    /// <returns>The version's definition content.</returns>
    /// <response code="200">Definition content retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such component, no such version of it, or the version has no stored content.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{sharedComponentId}/versions/{versionId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentVersionContentResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSharedComponentVersionContent(string sharedComponentId, string versionId)
    {
        ErrorOr<SharedComponentVersionContentResult> result = await _sender.Send(
            new GetSharedComponentVersionContentQuery(OrganizationId, UserId, sharedComponentId, versionId));

        _logger.LogInformation(
            "GetSharedComponentVersionContent {VersionId} of {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(versionId),
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            content => Ok(ToVersionContentResponse(content)),
            Problem);
    }

    /// <summary>
    /// Replaces a shared component's definition: the new definition becomes the head version and
    /// every linked consumer copy is rewritten in the same transaction.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="request">The definition, the expected head token, and orphan consent.</param>
    /// <remarks>
    /// The server re-normalizes the submitted definition, so per-consumer fields and link fields
    /// are stripped before it is stored. A definition byte-identical to the head is a dedup no-op
    /// (<c>wasDeduped: true</c>, nothing fanned out). A change that would delete consumer override
    /// rows or leave them inert is refused with <c>SharedComponent.OrphansNeedAcknowledgement</c>
    /// until re-sent with <c>acknowledgeSignatureOrphans: true</c>.
    /// </remarks>
    /// <returns>The component's new head tokens plus successor tokens for every rewritten consumer template.</returns>
    /// <response code="200">The definition is the new head and all consumers were rewritten.</response>
    /// <response code="400">The definition does not match the component's kind or cannot be read.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="409">The component is retired, the head moved, a concurrent append won, the chain has no versions, or orphaned overrides need acknowledgement.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPut("{sharedComponentId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentAppendResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveSharedComponentContent(
        string sharedComponentId,
        [FromBody] SaveSharedComponentContentRequest request)
    {
        ErrorOr<SharedComponentAppendResult> result = await _sender.Send(
            new SaveSharedComponentContentCommand(
                OrganizationId,
                UserId,
                sharedComponentId,
                request.Definition.GetRawText(),
                request.ExpectedHeadVersionId,
                request.AcknowledgeSignatureOrphans,
                request.AcknowledgeOverwrittenEntries));

        _logger.LogInformation(
            "SaveSharedComponentContent for {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            appended => Ok(SharedComponentResponseMapper.ToResponse(appended)),
            Problem);
    }

    /// <summary>
    /// Creates a brand-new shared component with its first version from a Library-authored definition.
    /// </summary>
    /// <param name="request">The kind, name, required category, optional description, and the definition JSON.</param>
    /// <remarks>
    /// The server re-normalizes the submitted definition, validates any referenced Block components,
    /// and stores it as the head of a fresh chain. No workflow anchor is involved and nothing is
    /// fanned out — a new component has no consumers yet.
    /// </remarks>
    /// <returns>The new component's identity and head tokens.</returns>
    /// <response code="200">The component was created.</response>
    /// <response code="400">The definition does not match the kind, cannot be read, or references an invalid target.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The category does not exist.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CreateSharedComponentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateSharedComponent([FromBody] CreateSharedComponentRequest request)
    {
        ErrorOr<CreateSharedComponentResult> result = await _sender.Send(
            new CreateSharedComponentCommand(
                OrganizationId,
                UserId,
                request.Name,
                request.Kind,
                request.CategoryId,
                request.Description,
                request.Definition.GetRawText()));

        _logger.LogInformation(
            "CreateSharedComponent {Kind} - {Outcome}",
            request.Kind,
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            created => Ok(SharedComponentResponseMapper.ToResponse(created)),
            Problem);
    }

    /// <summary>
    /// Previews the impact of changing a shared component's definition, without writing anything.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="request">Optionally, the draft definition to preview; omit the body or the field for blast radius only.</param>
    /// <remarks>
    /// Served over POST because the optional draft definition travels in the body; the call is a
    /// pure computation. Without a candidate the preview reports the blast radius every definition
    /// change shares — live consumers and the baseline registrations whose freshness a fan-out
    /// would stale. With one it also reports the slot delta against the stored head and the
    /// override rows the update would delete or leave inert. Previews are advisory: the apply and
    /// content writes re-compute everything inside their transactions.
    /// </remarks>
    /// <returns>The blast radius, and the candidate's slot delta and orphan set when one was supplied.</returns>
    /// <response code="200">Preview computed successfully.</response>
    /// <response code="400">The candidate definition does not match the component's kind or cannot be read.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such component, or a candidate was supplied and the head has no stored content to diff against.</response>
    /// <response code="409">The component's chain has no versions at all.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{sharedComponentId}/reconcile-preview")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentReconcilePreviewResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PreviewSharedComponentReconcile(
        string sharedComponentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PreviewSharedComponentReconcileRequest? request = null)
    {
        ErrorOr<SharedComponentReconcilePreviewResult> result = await _sender.Send(
            new PreviewSharedComponentReconcileQuery(
                OrganizationId,
                UserId,
                sharedComponentId,
                request?.CandidateDefinition?.GetRawText()));

        return result.Match<IActionResult>(
            preview => Ok(SharedComponentResponseMapper.ToResponse(preview)),
            Problem);
    }

    /// <summary>
    /// Makes a stored version the head again by re-appending its content, rewriting every linked
    /// consumer copy in the same transaction.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <param name="versionId">The version whose stored content becomes the new head.</param>
    /// <param name="request">The expected head token and orphan consent.</param>
    /// <remarks>
    /// Token-only by design: the server re-appends its own stored payload, so callers never
    /// round-trip definition content. Restoring the current head is a dedup no-op; restoring an
    /// older version appends a new head that repeats that version's content hash.
    /// </remarks>
    /// <returns>The component's new head tokens plus successor tokens for every rewritten consumer template.</returns>
    /// <response code="200">The version's content is the new head and all consumers were rewritten.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such component, or no such version of it.</response>
    /// <response code="409">The source version has no stored content, the component is retired, the head moved, a concurrent append won, or orphaned overrides need acknowledgement.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{sharedComponentId}/versions/{versionId}/restore")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentAppendResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RestoreSharedComponentVersion(
        string sharedComponentId,
        string versionId,
        [FromBody] RestoreSharedComponentVersionRequest request)
    {
        ErrorOr<SharedComponentAppendResult> result = await _sender.Send(
            new RestoreSharedComponentVersionCommand(
                OrganizationId,
                UserId,
                sharedComponentId,
                versionId,
                request.ExpectedHeadVersionId,
                request.AcknowledgeSignatureOrphans));

        _logger.LogInformation(
            "RestoreSharedComponentVersion {VersionId} of {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(versionId),
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            appended => Ok(SharedComponentResponseMapper.ToResponse(appended)),
            Problem);
    }

    /// <summary>
    /// Retires a shared component, breaking out every linked copy in the same transaction.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <remarks>
    /// A retired component has zero linked consumers by construction: each copy keeps its content
    /// and provenance but stops following the head. Retiring a block component also erases every
    /// definition-level reference to it: each section component whose head carries a ref entry to
    /// it gets a new version with that head content baked in as an inline slot, and the containers'
    /// linked instances are restamped to the rewritten heads — same transaction, listed in
    /// <c>inlinedContainerIds</c>. Retiring an already-retired component is an idempotent no-op.
    /// </remarks>
    /// <returns>The component's state plus successor tokens for every template the break-outs and restamps saved.</returns>
    /// <response code="200">The component is retired and every linked copy was broken out.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="409">A concurrent save touched a consumer template; retry.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{sharedComponentId}/retire")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentRetireResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RetireSharedComponent(string sharedComponentId)
    {
        ErrorOr<SharedComponentRetireResult> result = await _sender.Send(
            new RetireSharedComponentCommand(OrganizationId, UserId, sharedComponentId));

        _logger.LogInformation(
            "RetireSharedComponent {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            retired => Ok(SharedComponentResponseMapper.ToResponse(retired)),
            Problem);
    }

    /// <summary>
    /// Reactivates a retired shared component.
    /// </summary>
    /// <param name="sharedComponentId">The shared component ID.</param>
    /// <remarks>
    /// State-only: copies broken out by the retirement stay broken out. New consumers arrive by
    /// materializing the component again or relinking a matching broken-out copy through a
    /// reconcile apply. Unretiring an active component is an idempotent no-op.
    /// </remarks>
    /// <returns>The component's state; the break-out fields are always zero and empty here.</returns>
    /// <response code="200">The component is active again.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No shared component with this id exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{sharedComponentId}/unretire")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentRetireResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnretireSharedComponent(string sharedComponentId)
    {
        ErrorOr<SharedComponentRetireResult> result = await _sender.Send(
            new UnretireSharedComponentCommand(OrganizationId, UserId, sharedComponentId));

        _logger.LogInformation(
            "UnretireSharedComponent {SharedComponentId} - {Outcome}",
            LogSanitizer.ForLog(sharedComponentId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            unretired => Ok(SharedComponentResponseMapper.ToResponse(unretired)),
            Problem);
    }

    private static SharedComponentContentResponse ToContentResponse(SharedComponentContentResult content)
    {
        return new SharedComponentContentResponse(
            content.Id,
            content.HeadVersionId,
            content.HeadContentHash,
            ParseVerbatimJson(content.ContentJson));
    }

    private static SharedComponentVersionContentResponse ToVersionContentResponse(
        SharedComponentVersionContentResult content)
    {
        return new SharedComponentVersionContentResponse(
            content.SharedComponentId,
            content.VersionId,
            content.ContentHash,
            ParseVerbatimJson(content.ContentJson));
    }
}
