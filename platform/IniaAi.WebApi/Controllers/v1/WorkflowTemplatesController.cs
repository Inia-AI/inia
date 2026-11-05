using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Commands.SharedComponents.MaterializeSharedComponent;
using IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;
using IniaAi.Application.Commands.WorkflowTemplates;
using IniaAi.Application.Commands.WorkflowTemplates.CreateDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.DeleteDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.DeleteWorkflowType;
using IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;
using IniaAi.Application.Commands.WorkflowTemplates.RenameDocumentClass;
using IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;
using IniaAi.Application.Commands.WorkflowTemplates.UpdateDocumentClass;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateDocumentClasses;
using IniaAi.Application.Queries.WorkflowTemplates.ListWorkflowTemplates;
using IniaAi.Application.Queries.WorkflowTemplates.ValidateWorkflowTemplateContent;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;
using IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;
using IniaAi.Common.Text;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using IniaAi.WebApi.Contracts.Responses.WorkflowTransfer;
using IniaAi.WebApi.Contracts.Requests.SharedComponents;
using IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;
using IniaAi.WebApi.Contracts.Requests.WorkflowTransfer;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.Models.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using DomainDocumentClassRequirement = IniaAi.Domain.Entities.Workflows.Enums.DocumentClassRequirement;
using DomainReconcileAction = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentReconcileAction;
using DomainTransferArm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints for listing, reading, and editing workflow templates.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-templates")]
public sealed class WorkflowTemplatesController : ApiController
{
    private const string EDITOR_ROLES = Roles.EDITOR_ROLES;
    private const string AGENT_GUIDE_RESOURCE_NAME = "IniaAi.WebApi.Resources.WorkflowTemplatesAgentGuide.md";

    // The filename is build-protected (the csproj includes it explicitly, so a rename without a
    // csproj update fails the build). This suffix lets the lookup survive a namespace/folder move
    // without a silent 500.
    private const string AGENT_GUIDE_RESOURCE_SUFFIX = "WorkflowTemplatesAgentGuide.md";

    /// <summary>Upper bound on a saved workflow template payload (50 MB); generous for the largest
    /// real templates while bounding memory for a single request.</summary>
    private const int WORKFLOW_CONTENT_MAX_BYTES = 50 * 1024 * 1024;

    private const int VERSION_HISTORY_DEFAULT_TOP = 30;
    private const string TOP_PARAMETER = "top";
    private const string CURSOR_PARAMETER = "cursor";

    private readonly ILogger<WorkflowTemplatesController> _logger;
    private readonly ISender _sender;
    private readonly WorkflowNarrativeGenerator _narrativeGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowTemplatesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    /// <param name="narrativeGenerator">Turns a version diff into plain-language sentences.</param>
    public WorkflowTemplatesController(
        IRequestContext requestContext,
        ILogger<WorkflowTemplatesController> logger,
        ISender sender,
        WorkflowNarrativeGenerator narrativeGenerator)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
        _narrativeGenerator = narrativeGenerator;
    }

    /// <summary>
    /// Retrieves a workflow summary by workflow type ID for the CreateWorkflow page.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <remarks>
    /// Returns workflow details including Variables, DocumentClasses, and CounterpartyTypeMappings.
    /// This is a lightweight summary version for the CreateWorkflow page.
    /// Use this endpoint when user selects a workflow type to display form fields.
    /// </remarks>
    /// <returns>The workflow summary with Variables and DocumentClasses.</returns>
    /// <response code="200">Workflow summary retrieved successfully.</response>
    /// <response code="404">Workflow type not found or not accessible.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/summary")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSummary(string workflowTypeId)
    {
        ErrorOr<WorkflowDto> result = await _sender.Send(new GetWorkflowSummaryByTypeQuery(OrganizationId, workflowTypeId));

        _logger.LogInformation("GetSummary for WorkflowTypeId: {WorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Returns the agent guide: how to authenticate, read, edit, and save workflow
    /// templates through this API.
    /// </summary>
    /// <remarks>
    /// Intended as the first call for AI agents and scripts consuming this API. Returns
    /// markdown describing the read-edit-save loop, the template JSON structure, the
    /// optimistic-concurrency contract, and the editing rules enforced by validation.
    /// </remarks>
    /// <returns>The guide as <c>text/markdown</c>.</returns>
    /// <response code="200">Guide returned successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">The embedded guide resource is missing.</response>
    [HttpGet("agent-guide")]
    [Authorize(Roles = EDITOR_ROLES)]
    [Produces("text/markdown")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetAgentGuide()
    {
        System.Reflection.Assembly assembly = typeof(WorkflowTemplatesController).Assembly;
        string? resourceName = assembly.GetManifestResourceStream(AGENT_GUIDE_RESOURCE_NAME) is not null
            ? AGENT_GUIDE_RESOURCE_NAME
            : Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(AGENT_GUIDE_RESOURCE_SUFFIX, StringComparison.Ordinal));

        using Stream? stream = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            _logger.LogError("Embedded agent guide resource '{ResourceName}' not found", AGENT_GUIDE_RESOURCE_NAME);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Agent guide resource is missing.");
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "text/markdown");
    }

    /// <summary>
    /// Lists every workflow template type in the deployment.
    /// </summary>
    /// <remarks>
    /// Returns a flat list of workflow types (id, name, description, category) including
    /// types that do not have template content yet.
    /// </remarks>
    /// <returns>The list of workflow template summaries.</returns>
    /// <response code="200">Workflow template list retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<WorkflowTemplateSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkflowTemplates()
    {
        ErrorOr<List<WorkflowTemplateSummaryDto>> result = await _sender.Send(
            new ListWorkflowTemplatesQuery(OrganizationId, UserId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves the full current workflow template content for a workflow type.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <remarks>
    /// Returns the verbatim workflow template JSON plus the head version information.
    /// Pass the returned <c>versionId</c> back as <c>baseVersionId</c> when saving an edit;
    /// a null <c>versionId</c> means no version history exists yet, in which case the save
    /// must pass <c>baseVersionId: null</c> (omitting it is equivalent). AI agents should read
    /// <c>GET /workflow-templates/agent-guide</c> before editing.
    /// </remarks>
    /// <returns>The full workflow template content with head version info.</returns>
    /// <response code="200">Workflow template content retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type not found, or it has no content.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTemplateContentResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContent(string workflowTypeId)
    {
        ErrorOr<WorkflowTemplateContentResult> result = await _sender.Send(
            new GetWorkflowTemplateContentQuery(OrganizationId, UserId, workflowTypeId));

        _logger.LogInformation("GetContent for WorkflowTypeId: {WorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(
            content => Ok(ToContentResponse(content)),
            Problem);
    }

    /// <summary>
    /// Saves the full content of a workflow template, replacing the current version.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="request">The full workflow JSON, the base version id, and optional new-type metadata.</param>
    /// <remarks>
    /// Full-document save with optimistic concurrency: <c>baseVersionId</c> must match the
    /// current head version, otherwise 409 Conflict is returned with the current head info in
    /// the problem-details extensions (<c>currentHeadVersionId</c>, <c>currentContentHash</c>,
    /// <c>headCreatedAtUtc</c>) — re-read the content and re-apply
    /// the edit. When the workflow type does not exist it is created (upsert) using
    /// <c>newWorkflowType</c> metadata; a deleted type answers 404 instead, because its id stays
    /// taken until the weekly cleanup removes it. The payload is structurally validated; hard failures
    /// return 400 with field-level paths, soft issues are returned as warnings in the response.
    /// AI agents should read <c>GET /workflow-templates/agent-guide</c> before editing.
    /// </remarks>
    /// <returns>The save result with the new head version info and any warnings.</returns>
    /// <response code="200">Workflow template saved successfully.</response>
    /// <response code="400">The workflow JSON is malformed or structurally invalid.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The workflow type was deleted.</response>
    /// <response code="409">The supplied baseVersionId does not match the current head version.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPut("{workflowTypeId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [RequestSizeLimit(WORKFLOW_CONTENT_MAX_BYTES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SaveWorkflowTemplateContentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveContent(
        string workflowTypeId,
        [FromBody] SaveWorkflowTemplateContentRequest request)
    {
        if (request.Workflow.ValueKind != JsonValueKind.Object)
        {
            return Problem([Error.Validation("workflow", "The 'workflow' property must be a JSON object.")]);
        }

        ErrorOr<SaveWorkflowTemplateContentResult> result = await _sender.Send(
            new SaveWorkflowTemplateContentCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.Workflow.GetRawText(),
                request.BaseVersionId,
                request.NewWorkflowType?.Name,
                request.NewWorkflowType?.Description,
                request.NewWorkflowType?.CategoryId,
                request.ConfirmSignatureFieldDeletions));

        _logger.LogInformation("SaveContent for WorkflowTypeId: {WorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(
            saved => Ok(new SaveWorkflowTemplateContentResponse(
                saved.VersionId,
                saved.ContentHash,
                saved.WasDeduped,
                saved.CreatedWorkflowType,
                [.. saved.Warnings.Select(warning => new WorkflowTemplateWarningDto(warning.Path, warning.Message))])),
            Problem);
    }

    /// <summary>
    /// Runs a save's read-only checks against a candidate payload without writing anything.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="request">The same body you would send to <c>PUT /content</c>.</param>
    /// <remarks>
    /// A rehearsal of the save: it answers with the very statuses the save would answer with, so the
    /// error handling you already wrote for <c>PUT /content</c> works here unchanged — <b>400</b> with
    /// the same field-level paths, <b>409</b> with the same current-head extensions, <b>200</b> when
    /// the payload would be accepted. The 200 body adds <c>wouldCreateWorkflowType</c>, which earns
    /// the call when saving with a null <c>baseVersionId</c>: the save upserts, so a mistyped id
    /// creates a second workflow type there instead of refusing. With a <c>baseVersionId</c> in hand
    /// a mistyped id already 409s, because the type it names has no head.
    ///
    /// It is deliberately narrower than the save. It does not report whether the save would dedupe,
    /// nor whether signature-field deletions would need confirming; both are decided only after the
    /// template graph is replaced. Neither is dangerous to meet on the real save — a dedupe is a
    /// no-op, and an unconfirmed signature deletion is refused with nothing written. Like every
    /// preview here the answer is advisory: the save re-runs all of it inside its own transaction, so
    /// a concurrent write can still turn an accepted rehearsal into a 409.
    /// </remarks>
    /// <returns>What the save would do, when it would be accepted.</returns>
    /// <response code="200">The payload would be accepted.</response>
    /// <response code="400">The workflow JSON is malformed or structurally invalid.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The workflow type was deleted.</response>
    /// <response code="409">The supplied baseVersionId does not match the current head version.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/content/validate")]
    [Authorize(Roles = EDITOR_ROLES)]
    [RequestSizeLimit(WORKFLOW_CONTENT_MAX_BYTES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ValidateWorkflowTemplateContentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ValidateContent(
        string workflowTypeId,
        [FromBody] SaveWorkflowTemplateContentRequest request)
    {
        if (request.Workflow.ValueKind != JsonValueKind.Object)
        {
            return Problem([Error.Validation("workflow", "The 'workflow' property must be a JSON object.")]);
        }

        ErrorOr<ValidateWorkflowTemplateContentResult> result = await _sender.Send(
            new ValidateWorkflowTemplateContentQuery(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.Workflow.GetRawText(),
                request.BaseVersionId,
                request.NewWorkflowType?.Name,
                request.NewWorkflowType?.CategoryId));

        _logger.LogInformation("ValidateContent for WorkflowTypeId: {WorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Rejected - {result.FirstError.Description}" : "Would be accepted"));

        return result.Match<IActionResult>(
            validated => Ok(new ValidateWorkflowTemplateContentResponse(
                validated.WouldCreateWorkflowType,
                validated.HeadVersionId,
                [.. validated.Warnings.Select(warning => new WorkflowTemplateWarningDto(warning.Path, warning.Message))])),
            Problem);
    }

    /// <summary>
    /// Lists a workflow template's version history, newest first.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="top">How many versions to return (1 to 200, default 30).</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page, to read older versions. Omit for the newest page.</param>
    /// <remarks>
    /// Every save appends one version, so the list answers what changed, when, and by whom: each
    /// entry carries its author, its content hash and the plain-language narrative stored at save
    /// time. <c>parentVersionId</c> is the preceding version in the chain. Versions saved before
    /// the per-organization chains were merged carry a chronological pointer there, so read it as
    /// chain order, never as proof of which version an author edited from. Entries with
    /// <c>payloadStored: false</c> had their workflow JSON cleared by retention: they still list
    /// here, but their content and comparisons, and the changes of any version that needs their
    /// content, answer <c>WorkflowVersion.PayloadTrimmed</c>.
    /// </remarks>
    /// <returns>One page of the version history.</returns>
    /// <response code="200">Version history retrieved successfully.</response>
    /// <response code="400">The <c>top</c> or <c>cursor</c> value is invalid.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No workflow type with this id exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/versions")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTemplateVersionsResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVersions(
        string workflowTypeId,
        [FromQuery] int top = VERSION_HISTORY_DEFAULT_TOP,
        [FromQuery] string? cursor = null)
    {
        if (top is < 1 or > GetWorkflowHistoryQueryValidator.MAX_TAKE)
        {
            return Problem([Error.Validation(
                TOP_PARAMETER, $"'top' must be between 1 and {GetWorkflowHistoryQueryValidator.MAX_TAKE}.")]);
        }

        WorkflowVersionCursor position = default;
        if (cursor is not null && !WorkflowVersionCursor.TryParse(cursor, out position))
        {
            return Problem([Error.Validation(
                CURSOR_PARAMETER, "'cursor' must be a nextCursor value returned by this endpoint.")]);
        }

        ErrorOr<WorkflowHistoryPageDto> result = await _sender.Send(
            new GetWorkflowHistoryQuery(
                OrganizationId,
                workflowTypeId,
                top,
                cursor is null ? null : position.CreatedAtUtc,
                cursor is null ? null : position.VersionId,
                RequireKnownType: true));

        _logger.LogInformation(
            "GetVersions of {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            page => Ok(WorkflowTemplateVersionResponseMapper.ToResponse(workflowTypeId, page)),
            Problem);
    }

    /// <summary>
    /// Retrieves the workflow template JSON of one stored version.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="versionId">The version whose content to serve.</param>
    /// <remarks>
    /// The head-content contract extended to any version in the chain: the stored workflow JSON is
    /// served verbatim. To restore a version, PUT its <c>workflow</c> to
    /// <c>/{workflowTypeId}/content</c> with the <b>current head</b> <c>versionId</c> (from
    /// <c>GET /content</c>) as <c>baseVersionId</c>; the restore is appended as a new version.
    /// </remarks>
    /// <returns>The version's workflow template content.</returns>
    /// <response code="200">Version content retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such version on this workflow type (<c>WorkflowVersion.NotFound</c>), or its content was cleared by retention (<c>WorkflowVersion.PayloadTrimmed</c>) or is absent (<c>WorkflowVersion.PayloadMissing</c>).</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/versions/{versionId}/content")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTemplateVersionContentResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVersionContent(string workflowTypeId, string versionId)
    {
        ErrorOr<WorkflowVersionContentDto> result = await _sender.Send(
            new GetWorkflowVersionQuery(OrganizationId, versionId, workflowTypeId));

        _logger.LogInformation(
            "GetVersionContent {VersionId} of {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(versionId),
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            version => Ok(new WorkflowTemplateVersionContentResponse(
                version.WorkflowTypeId,
                version.Id,
                version.ContentHash,
                version.ParentVersionId,
                version.CreatedAtUtc,
                version.CreatedByUserId,
                version.Narrative,
                ParseVerbatimJson(version.WorkflowJson))),
            Problem);
    }

    /// <summary>
    /// Returns what one version changed relative to the version before it in the chain.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="versionId">The version whose changes to compute.</param>
    /// <remarks>
    /// The diff reads from the version's <c>parentVersionId</c> to the version itself: sections,
    /// blocks, variables, document classes and workflow-level fields, with full before and after
    /// values per changed field, plus one plain-language sentence per changed element. The first
    /// version of a type has nothing to diff against and answers 200 with
    /// <c>isFirstVersion: true</c> and a null <c>diff</c>. For versions saved before the
    /// per-organization chains were merged, the preceding version is the chronologically previous
    /// save, so the diff is a true content difference but not necessarily one author's edit.
    /// </remarks>
    /// <returns>The structured diff and its narrative.</returns>
    /// <response code="200">Changes computed successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">No such version on this workflow type (<c>WorkflowVersion.NotFound</c>), or the content of the version or of its predecessor was cleared by retention (<c>WorkflowVersion.PayloadTrimmed</c>) or is absent (<c>WorkflowVersion.PayloadMissing</c>).</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/versions/{versionId}/changes")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTemplateVersionDiffResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVersionChanges(string workflowTypeId, string versionId)
    {
        ErrorOr<WorkflowVersionChangesResult> result = await _sender.Send(
            new GetWorkflowVersionChangesQuery(OrganizationId, versionId, workflowTypeId, FailWhenUnavailable: true));

        _logger.LogInformation(
            "GetVersionChanges {VersionId} of {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(versionId),
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            changes => Ok(WorkflowTemplateVersionResponseMapper.ToChangesResponse(workflowTypeId, changes, _narrativeGenerator)),
            Problem);
    }

    /// <summary>
    /// Returns what differs between any two versions of one workflow template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="from">The "before" version.</param>
    /// <param name="to">The "after" version.</param>
    /// <remarks>
    /// The same diff shape as the changes route, read from <c>from</c> to <c>to</c> exactly as
    /// given, so passing a newer <c>from</c> previews what restoring <c>to</c> would undo. Both
    /// versions must belong to this workflow type.
    /// </remarks>
    /// <returns>The structured diff and its narrative.</returns>
    /// <response code="200">Comparison computed successfully.</response>
    /// <response code="400"><c>from</c> or <c>to</c> is missing.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">A version does not exist on this workflow type (<c>WorkflowVersion.NotFound</c>), or its content was cleared by retention (<c>WorkflowVersion.PayloadTrimmed</c>) or is absent (<c>WorkflowVersion.PayloadMissing</c>).</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/versions/compare")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTemplateVersionDiffResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CompareVersions(
        string workflowTypeId,
        [FromQuery] string from,
        [FromQuery] string to)
    {
        ErrorOr<WorkflowDiff> result = await _sender.Send(
            new CompareWorkflowVersionsQuery(OrganizationId, from, to, workflowTypeId, PreserveArgumentOrder: true));

        _logger.LogInformation(
            "CompareVersions {FromVersionId} to {ToVersionId} of {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(from),
            LogSanitizer.ForLog(to),
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            diff => Ok(WorkflowTemplateVersionResponseMapper.ToCompareResponse(workflowTypeId, from, to, diff, _narrativeGenerator)),
            Problem);
    }

    /// <summary>
    /// Duplicates a workflow template into a new workflow type.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier to duplicate.</param>
    /// <param name="request">The new type's identifier and metadata.</param>
    /// <remarks>
    /// The duplicate keeps every shared-component link live, every per-entry deviation, and every
    /// stable id, so it stays diffable against its source. It starts with no organization access,
    /// no signature override values, and no execution history; the response lists what did not
    /// carry.
    /// </remarks>
    /// <returns>The new type's identifier, its root version, and what was skipped.</returns>
    /// <response code="200">Workflow type duplicated successfully.</response>
    /// <response code="400">The request is invalid (e.g. the new id matches the source, or the category does not exist).</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The source workflow type does not exist or has no content.</response>
    /// <response code="409">A workflow type with the requested identifier already exists.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/duplicate")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DuplicateWorkflowTypeResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DuplicateWorkflowType(
        string workflowTypeId,
        [FromBody] DuplicateWorkflowTypeRequest request)
    {
        ErrorOr<DuplicateWorkflowTypeResult> result = await _sender.Send(
            new DuplicateWorkflowTypeCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.NewWorkflowTypeId,
                request.Name,
                request.Description,
                request.CategoryId));

        _logger.LogInformation(
            "DuplicateWorkflowType {WorkflowTypeId} into {NewWorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(request.NewWorkflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(
            duplicated => Ok(new DuplicateWorkflowTypeResponse(
                duplicated.NewWorkflowTypeId,
                duplicated.VersionId,
                duplicated.ContentHash,
                [.. duplicated.SkippedCarriers.Select(skipped =>
                    new SkippedCarrierDto(skipped.Carrier, skipped.Reason))])),
            Problem);
    }

    /// <summary>
    /// Deletes a workflow type that only the caller's organization uses.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type to delete.</param>
    /// <remarks>
    /// The type disappears from the organization at once, together with its runs, their reports,
    /// its validation cases, and the documents uploaded for it that no run of another type uses.
    /// The weekly cleanup removes all of it for good, the template and its version history
    /// included, once the organization's retention window has passed. A type another organization
    /// also uses, a type with a run in progress, and the platform's reserved types are refused.
    /// </remarks>
    /// <response code="204">Workflow type deleted.</response>
    /// <response code="400">The workflow type is reserved by the platform.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The workflow type does not exist, or the caller's organization cannot reach it.</response>
    /// <response code="409">Another organization uses the type too, or a run of it is in progress.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("{workflowTypeId}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteWorkflowType(string workflowTypeId)
    {
        ErrorOr<Deleted> result = await _sender.Send(new DeleteWorkflowTypeCommand(OrganizationId, UserId, workflowTypeId));

        _logger.LogInformation(
            "DeleteWorkflowType {WorkflowTypeId} - Result: {Result}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Exports a workflow template as a self-contained transfer bundle.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type to export.</param>
    /// <remarks>
    /// The bundle carries the template's stored definition plus every shared component it depends on
    /// — each at its head, with the fingerprint trail import uses to reconcile against the target's
    /// own copies. Signature values, organization access, counterparty mappings, execution history,
    /// and per-type prompt tuning do not travel. Import the bundle on another deployment with
    /// <c>import-preview</c> then <c>import-apply</c>.
    /// </remarks>
    /// <returns>The transfer bundle.</returns>
    /// <response code="200">The bundle was produced.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The workflow type does not exist, has no content, or a required component payload is absent.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/export")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTransferBundle))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportWorkflowTemplate(string workflowTypeId)
    {
        ErrorOr<ExportWorkflowTemplateResult> result = await _sender.Send(
            new ExportWorkflowTemplateQuery(OrganizationId, UserId, workflowTypeId));

        return result.Match<IActionResult>(export => Ok(export.Bundle), Problem);
    }

    /// <summary>
    /// Previews importing a transfer bundle into a workflow type without writing anything.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type to import into. It does not have to exist yet — an absent or headless target previews as a create.</param>
    /// <param name="bundle">The transfer bundle produced by another deployment's export.</param>
    /// <remarks>
    /// Computes how the bundle's template and each shared component relate to this deployment's own
    /// copies — new, in sync, local-ahead, bundle-ahead, or diverged — and returns the base version
    /// token to apply the import with. Nothing is written; the apply path re-computes everything
    /// inside its transaction, so a preview goes stale by design.
    /// </remarks>
    /// <returns>The import preview.</returns>
    /// <response code="200">The preview was computed.</response>
    /// <response code="400">The bundle is malformed, incomplete, or a newer format than supported.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/import-preview")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTransferPreviewResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ImportWorkflowTemplatePreview(
        string workflowTypeId,
        [FromBody] WorkflowTransferBundle bundle)
    {
        ErrorOr<ImportWorkflowTemplatePreviewResult> result = await _sender.Send(
            new ImportWorkflowTemplatePreviewQuery(OrganizationId, UserId, workflowTypeId, bundle));

        _logger.LogInformation(
            "ImportWorkflowTemplatePreview into {WorkflowTypeId} from {OriginWorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(bundle?.OriginWorkflowTypeId ?? string.Empty),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            preview => Ok(SharedComponentResponseMapper.ToResponse(preview)),
            Problem);
    }

    /// <summary>
    /// Applies a transfer bundle to a workflow type, resolving each element with the caller's arms.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type to import into.</param>
    /// <param name="request">The bundle plus the take-theirs / keep-mine decisions from the preview.</param>
    /// <remarks>
    /// One transaction: new components are created, take-theirs components append the bundle head and
    /// fan out to every local consumer, and the template arm either adopts the bundle's content or
    /// keeps the local one. An absent or headless target is created from the bundle instead of
    /// refused — omit baseVersionId there. A stale base version or component head, an unacknowledged unrelated
    /// history, a retired local component, or newly orphaned signature fields each return 409 — re-run
    /// import-preview and retry.
    /// </remarks>
    /// <returns>What apply did to the template and each component.</returns>
    /// <response code="200">The import was applied.</response>
    /// <response code="400">The bundle is malformed, incomplete, or a newer format than supported.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">The kept local template or required local content was not found.</response>
    /// <response code="409">A head moved, history is unrelated, a component is retired, or signatures are orphaned.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/import-apply")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowTransferApplyResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ImportWorkflowTemplateApply(
        string workflowTypeId,
        [FromBody] ImportWorkflowTemplateApplyRequest request)
    {
        IReadOnlyList<ImportComponentDecisionDto> decisions = request.ComponentDecisions ?? [];
        ErrorOr<ImportWorkflowTemplateApplyResult> result = await _sender.Send(
            new ImportWorkflowTemplateApplyCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.Bundle,
                request.BaseVersionId,
                (DomainTransferArm)(int)request.TemplateArm,
                request.AcknowledgeUnrelatedHistory,
                request.AcknowledgeSignatureOrphans,
                [.. decisions.Select(ToDecision)]));

        _logger.LogInformation(
            "ImportWorkflowTemplateApply into {WorkflowTypeId} from {OriginWorkflowTypeId} with {DecisionCount} decisions - {Outcome}",
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(request.Bundle?.OriginWorkflowTypeId ?? string.Empty),
            decisions.Count,
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            applied => Ok(SharedComponentResponseMapper.ToResponse(applied)),
            Problem);
    }

    private static ImportComponentDecision ToDecision(ImportComponentDecisionDto decision)
    {
        return new ImportComponentDecision(
            decision.ComponentId,
            (DomainTransferArm)(int)decision.Arm,
            decision.ExpectedLocalHeadVersionId);
    }

    /// <summary>
    /// Lists the document classes defined on a workflow template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <remarks>
    /// Returns each document class plus the template's head <c>versionId</c>. Send that value back as
    /// <c>baseVersionId</c> when creating, updating, renaming, or deleting a document class so a
    /// concurrent edit is detected (409 Conflict on a stale value).
    /// </remarks>
    /// <returns>The document classes and the head version id.</returns>
    /// <response code="200">Document classes retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/document-classes")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassListResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListDocumentClasses(string workflowTypeId)
    {
        ErrorOr<DocumentClassListResult> result = await _sender.Send(
            new GetWorkflowTemplateDocumentClassesQuery(OrganizationId, UserId, workflowTypeId));

        return result.Match<IActionResult>(
            list => Ok(ToListResponse(list)),
            Problem);
    }

    /// <summary>
    /// Retrieves a single document class from a workflow template by key.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="key">The document class key.</param>
    /// <returns>The document class.</returns>
    /// <response code="200">Document class retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found, or the class was not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/document-classes/{key}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDocumentClass(string workflowTypeId, string key)
    {
        ErrorOr<DocumentClassListResult> result = await _sender.Send(
            new GetWorkflowTemplateDocumentClassesQuery(OrganizationId, UserId, workflowTypeId));
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        DocumentClassView? match = result.Value.DocumentClasses.FirstOrDefault(
            dc => dc.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return Problem([Error.NotFound("DocumentClass.NotFound", $"Document class '{key}' was not found.")]);
        }

        return Ok(ToDto(match));
    }

    /// <summary>
    /// Creates a document class on a workflow template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="request">The document class fields and the base version id.</param>
    /// <remarks>
    /// Optimistic concurrency: pass the head <c>baseVersionId</c> (from listing the document classes).
    /// It is required whenever a head version exists; a stale or missing value is rejected with 409
    /// Conflict. Omit it only for a template with no version history yet.
    /// </remarks>
    /// <returns>The created document class and the new head version info.</returns>
    /// <response code="201">Document class created.</response>
    /// <response code="400">The request is invalid or the key is reserved.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found.</response>
    /// <response code="409">The key already exists, or the baseVersionId is stale or missing.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/document-classes")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(DocumentClassMutationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateDocumentClass(
        string workflowTypeId,
        [FromBody] CreateDocumentClassRequest request)
    {
        ErrorOr<DocumentClassMutationResult> result = await _sender.Send(
            new CreateDocumentClassCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.Key,
                request.DisplayName,
                request.Description ?? string.Empty,
                request.DocumentCategoryId,
                (DomainDocumentClassRequirement)(int)request.Requirement,
                request.ShouldBeSingular,
                request.IsOutputDocument,
                request.BaseVersionId,
                request.LlmDescription));

        LogDocumentClassResult(nameof(CreateDocumentClass), request.Key, workflowTypeId, result);

        return result.Match<IActionResult>(
            mutation => CreatedAtAction(
                nameof(GetDocumentClass),
                new { workflowTypeId, key = mutation.Key },
                ToMutationResponse(mutation)),
            Problem);
    }

    /// <summary>
    /// Updates fields of an existing document class on a workflow template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="key">The document class key to update.</param>
    /// <param name="request">The fields to change; any field left null is left unchanged. Updating never renames.</param>
    /// <remarks>
    /// A field omitted from the body is left unchanged, so a partial update never resets fields the
    /// caller did not send. Optimistic concurrency: pass the head <c>baseVersionId</c>; a stale or
    /// missing value is rejected with 409 Conflict.
    /// </remarks>
    /// <returns>The updated document class and the new head version info.</returns>
    /// <response code="200">Document class updated.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found, or the class was not found.</response>
    /// <response code="409">The baseVersionId is stale or missing.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPatch("{workflowTypeId}/document-classes/{key}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassMutationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateDocumentClass(
        string workflowTypeId,
        string key,
        [FromBody] UpdateDocumentClassRequest request)
    {
        ErrorOr<DocumentClassMutationResult> result = await _sender.Send(
            new UpdateDocumentClassCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                key,
                request.DisplayName,
                request.Requirement.HasValue ? (DomainDocumentClassRequirement)(int)request.Requirement.Value : null,
                request.ShouldBeSingular,
                request.IsOutputDocument,
                request.Description,
                request.DocumentCategoryId,
                request.BaseVersionId,
                request.LlmDescription));

        LogDocumentClassResult(nameof(UpdateDocumentClass), key, workflowTypeId, result);

        return result.Match<IActionResult>(OkMutation, Problem);
    }

    /// <summary>
    /// Renames a document class on a workflow template, cascading the new key into every reference.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="key">The current document class key.</param>
    /// <param name="request">The new key and the base version id.</param>
    /// <remarks>
    /// The new key cascades into every block and parameterized-section reference; all other fields of
    /// the class are preserved. Optimistic concurrency: pass the head <c>baseVersionId</c>; a stale or
    /// missing value is rejected with 409 Conflict.
    /// </remarks>
    /// <returns>The renamed document class and the new head version info.</returns>
    /// <response code="200">Document class renamed.</response>
    /// <response code="400">The request is invalid or the new key is reserved.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found, or the class was not found.</response>
    /// <response code="409">The new key already exists, or the baseVersionId is stale or missing.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/document-classes/{key}/rename")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassMutationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RenameDocumentClass(
        string workflowTypeId,
        string key,
        [FromBody] RenameDocumentClassRequest request)
    {
        ErrorOr<DocumentClassMutationResult> result = await _sender.Send(
            new RenameDocumentClassCommand(OrganizationId, UserId, workflowTypeId, key, request.NewKey, request.BaseVersionId));

        LogDocumentClassResult(nameof(RenameDocumentClass), key, workflowTypeId, result);

        return result.Match<IActionResult>(OkMutation, Problem);
    }

    /// <summary>
    /// Removes a document class from a workflow template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="key">The document class key to remove.</param>
    /// <param name="baseVersionId">The head version id the delete is based on, for optimistic concurrency.</param>
    /// <remarks>
    /// The removal cascades: the key is removed from every block and parameterized-section reference;
    /// a block or variant left with no supported classes falls back to accepting any document, and
    /// output-class references to the key are cleared so those blocks stop producing documents.
    /// Optimistic concurrency: pass the head <c>baseVersionId</c>; a stale or missing value is rejected
    /// with 409 Conflict.
    /// </remarks>
    /// <returns>The mutation result with the new head version info.</returns>
    /// <response code="200">Document class removed.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type or its content not found, or the class was not found.</response>
    /// <response code="409">The baseVersionId is stale or missing.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("{workflowTypeId}/document-classes/{key}")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassMutationResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteDocumentClass(
        string workflowTypeId,
        string key,
        [FromQuery] string? baseVersionId)
    {
        ErrorOr<DocumentClassMutationResult> result = await _sender.Send(
            new DeleteDocumentClassCommand(OrganizationId, UserId, workflowTypeId, key, baseVersionId));

        LogDocumentClassResult(nameof(DeleteDocumentClass), key, workflowTypeId, result);

        return result.Match<IActionResult>(OkMutation, Problem);
    }

    private IActionResult OkMutation(DocumentClassMutationResult mutation)
    {
        return Ok(ToMutationResponse(mutation));
    }

    private void LogDocumentClassResult(
        string action,
        string key,
        string workflowTypeId,
        ErrorOr<DocumentClassMutationResult> result)
    {
        _logger.LogInformation("{Action} '{Key}' on {WorkflowTypeId} - Result: {Result}",
            action,
            LogSanitizer.ForLog(key),
            LogSanitizer.ForLog(workflowTypeId),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));
    }

    private static DocumentClassMutationResponse ToMutationResponse(DocumentClassMutationResult mutation)
    {
        return new DocumentClassMutationResponse(
            mutation.VersionId,
            mutation.ContentHash,
            mutation.WasDeduped,
            mutation.Key,
            mutation.DocumentClass is null ? null : ToDto(mutation.DocumentClass));
    }

    private static DocumentClassListResponse ToListResponse(DocumentClassListResult list)
    {
        return new DocumentClassListResponse(
            list.WorkflowTypeId,
            list.VersionId,
            [.. list.DocumentClasses.Select(ToDto)]);
    }

    private static DocumentClassDto ToDto(DocumentClassView view)
    {
        return new DocumentClassDto
        {
            Key = view.Key,
            DisplayName = view.DisplayName,
            Description = view.Description,
            Requirement = view.Requirement.ToString(),
            ShouldBeSingular = view.ShouldBeSingular,
            IsOutputDocument = view.IsOutputDocument,
            DocumentCategoryId = view.DocumentCategoryId,
            LlmDescription = view.LlmDescription,
        };
    }

    /// <summary>
    /// Turns an existing template block or section into a Library component and links it in place.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="request">The target locator, component metadata or attach target, and base version.</param>
    /// <remarks>
    /// Address the target by canonical node path (<c>Sections[i]</c> or <c>Sections[i].Blocks[j]</c>,
    /// exactly as served by the content GET) or by stable id — exactly one of the two. With
    /// <c>attachToComponentId</c> the node is linked to that existing component instead of creating
    /// one; its head content must match the node's normalized content exactly. The server stamps all
    /// link fields — never write them by hand. A link-only promote appends a new template version
    /// with an unchanged content hash. Promoting a definition-slot block inside a Linked section
    /// additionally converts that section component's slot from inline to a reference and fans the
    /// change out to every consumer; that container append requires
    /// <c>expectedContainerHeadVersionId</c>.
    /// </remarks>
    /// <returns>The component and new template head tokens.</returns>
    /// <response code="200">The node is now linked to a Library component.</response>
    /// <response code="400">The target locator is invalid or the node cannot be promoted.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type, target node, or attach component not found.</response>
    /// <response code="409">Stale base version or container head, node already shared, or attach content mismatch.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/shared-components/promote")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PromoteSharedComponentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PromoteSharedComponent(
        string workflowTypeId,
        [FromBody] PromoteSharedComponentRequest request)
    {
        ErrorOr<PromoteSharedComponentResult> result = await _sender.Send(new PromoteSharedComponentCommand(
            OrganizationId,
            UserId,
            workflowTypeId,
            request.TargetPath,
            request.TargetStableId,
            request.Name,
            request.Description,
            request.AttachToComponentId,
            request.BaseVersionId,
            request.CategoryId,
            request.ExpectedContainerHeadVersionId));

        _logger.LogInformation(
            "PromoteSharedComponent in {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            promoted => Ok(new PromoteSharedComponentResponse(
                promoted.SharedComponentId,
                promoted.Name,
                promoted.Kind,
                promoted.ComponentVersionHash,
                promoted.WasAttached,
                promoted.TargetPath,
                promoted.VersionId,
                promoted.ContentHash,
                promoted.WasDeduped)),
            Problem);
    }

    /// <summary>
    /// Inserts a linked copy of a Library component into the template.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <param name="request">The component, landing position, optional tag, and base version.</param>
    /// <remarks>
    /// The server performs the whole materialization — definition copy, fresh stable ids,
    /// tag-collision rewrite (recorded in <c>bindings</c>; an absent key means identity), and link
    /// stamping. Block components land in the section addressed by <c>targetSectionPath</c> or
    /// <c>targetSectionStableId</c>; section components land at <c>index</c> in the workflow and
    /// take no section locator. Non-blocking structural issues (such as definition references to
    /// tags this template lacks) are returned as warnings.
    /// </remarks>
    /// <returns>The landed node's address and the new template head tokens.</returns>
    /// <response code="200">The component was materialized into the template.</response>
    /// <response code="400">The landing target is invalid for the component's kind.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type, target section, or component not found.</response>
    /// <response code="409">Stale base version, retired component, or missing head content.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/shared-components/materialize")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MaterializeSharedComponentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MaterializeSharedComponent(
        string workflowTypeId,
        [FromBody] MaterializeSharedComponentRequest request)
    {
        ErrorOr<MaterializeSharedComponentResult> result = await _sender.Send(new MaterializeSharedComponentCommand(
            OrganizationId,
            UserId,
            workflowTypeId,
            request.SharedComponentId,
            request.TargetSectionPath,
            request.TargetSectionStableId,
            request.Index,
            request.ReplacementTag,
            request.BaseVersionId));

        _logger.LogInformation(
            "MaterializeSharedComponent {SharedComponentId} into {WorkflowTypeId} - {Outcome}",
            LogSanitizer.ForLog(request.SharedComponentId),
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            materialized => Ok(new MaterializeSharedComponentResponse(
                materialized.SharedComponentId,
                materialized.ComponentVersionHash,
                materialized.LandedPath,
                materialized.LandedReplacementTag,
                materialized.Bindings is null ? null : new Dictionary<string, string>(materialized.Bindings),
                materialized.VersionId,
                materialized.ContentHash,
                materialized.WasDeduped,
                [.. materialized.Warnings.Select(warning => new WorkflowTemplateWarningDto(warning.Path, warning.Message))])),
            Problem);
    }

    /// <summary>
    /// Lists the template's reconcile surface: every block or section carrying a shared-component
    /// link record, classified against the live registry.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID.</param>
    /// <remarks>
    /// Pure read. Each row carries both address forms, the instance/link/head hash triple, the
    /// relation classification, and the sanctioned decisions ordered default-first. The response's
    /// <c>baseVersionId</c> is the anchor token a reconcile apply must echo.
    /// </remarks>
    /// <returns>The reconcile surface with the anchor tokens.</returns>
    /// <response code="200">Reconcile surface computed successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{workflowTypeId}/shared-components/reconcile")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowSharedComponentReconcileResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkflowSharedComponentReconcile(string workflowTypeId)
    {
        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _sender.Send(
            new GetWorkflowSharedComponentReconcileQuery(OrganizationId, UserId, workflowTypeId));

        return result.Match<IActionResult>(
            surface => Ok(SharedComponentResponseMapper.ToResponse(surface)),
            Problem);
    }

    /// <summary>
    /// Applies a batch of per-instance reconcile decisions atomically, with every fan-out they
    /// trigger.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type ID of the anchor template the decisions address.</param>
    /// <param name="request">The anchor base version and the per-instance decisions.</param>
    /// <remarks>
    /// Everything commits in one transaction or nothing does. An <c>UpdateShared</c> decision
    /// pushes that instance's content as the component's new head and rewrites every linked
    /// consumer — including instances and templates the decisions never mentioned.
    /// <c>UpdateShared</c>, <c>AdoptHead</c>, and <c>Relink</c> must echo the component head they
    /// were decided against via <c>expectedHeadVersionId</c>. Decisions that would orphan
    /// signature overrides are refused until re-sent with
    /// <c>acknowledgeSignatureOrphans: true</c>.
    /// </remarks>
    /// <returns>Per-decision outcomes plus successor tokens for every template and component head the apply touched.</returns>
    /// <response code="200">All decisions applied atomically.</response>
    /// <response code="400">A locator is invalid, a decision addresses a node that is not a copy of its component, or the decisions conflict.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Caller lacks an editor role.</response>
    /// <response code="404">Workflow type, target node, or component not found.</response>
    /// <response code="409">Stale anchor or component head, retired component, content mismatch on relink, concurrent save, or orphaned overrides need acknowledgement.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpPost("{workflowTypeId}/shared-components/reconcile/apply")]
    [Authorize(Roles = EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SharedComponentReconcileApplyResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ApplySharedComponentReconcile(
        string workflowTypeId,
        [FromBody] ApplySharedComponentReconcileRequest request)
    {
        ErrorOr<SharedComponentReconcileApplyResult> result = await _sender.Send(
            new ApplySharedComponentReconcileCommand(
                OrganizationId,
                UserId,
                workflowTypeId,
                request.BaseVersionId,
                [.. request.Decisions.Select(ToDecision)]));

        _logger.LogInformation(
            "ApplySharedComponentReconcile in {WorkflowTypeId} with {DecisionCount} decisions - {Outcome}",
            LogSanitizer.ForLog(workflowTypeId),
            request.Decisions.Count,
            result.IsError ? result.FirstError.Code : "Success");

        return result.Match<IActionResult>(
            applied => Ok(SharedComponentResponseMapper.ToResponse(applied)),
            Problem);
    }

    private static SharedComponentReconcileDecision ToDecision(SharedComponentReconcileDecisionDto decision)
    {
        return new SharedComponentReconcileDecision(
            decision.SharedComponentId,
            decision.TargetStableId,
            decision.TargetPath,
            (DomainReconcileAction)(int)decision.Action,
            decision.ExpectedHeadVersionId,
            decision.AcknowledgeSignatureOrphans,
            decision.EntryKey,
            decision.EntryStableId,
            decision.ExpectedRefHeadVersionId,
            decision.AcknowledgeOverwrittenEntries);
    }

    private static WorkflowTemplateContentResponse ToContentResponse(WorkflowTemplateContentResult content)
    {
        return new WorkflowTemplateContentResponse(
            content.WorkflowTypeId,
            content.VersionId,
            content.ContentHash,
            content.CreatedAtUtc,
            content.CreatedByUserId,
            ParseVerbatimJson(content.WorkflowJson));
    }
}
