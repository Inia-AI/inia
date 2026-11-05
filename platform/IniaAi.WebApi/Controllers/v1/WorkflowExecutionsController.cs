using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.Signatures.TestSignaturePrompt;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;
using IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;
using IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;
using IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;
using IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLog;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutionLogs;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Services.Abstractions.Services;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Requests.Signatures;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.Jobs;
using IniaAi.WebApi.ExampleProviders.Workflow;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints for managing workflows and launching workflow jobs.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-executions")]
public sealed class WorkflowExecutionsController : ApiController
{
    private readonly ILogger<WorkflowExecutionsController> _logger;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowExecutionsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="currentUser">The current user service instance.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public WorkflowExecutionsController(
        IRequestContext requestContext,
        ILogger<WorkflowExecutionsController> logger,
        ICurrentUserService currentUser,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _currentUser = currentUser;
        _sender = sender;
    }

    /// <summary>
    /// Starts a new execution instance of a workflow.
    /// </summary>
    /// <remarks>
    /// This creates a new background job from the specified workflow template and starts its execution.
    /// The output is returned as a workflow output DTO including its Job ID.
    /// </remarks>
    /// <param name="request">The request body containing input parameters and document references.</param>
    /// <returns>The result of the workflow job creation.</returns>
    /// <response code="201">Job created and workflow started successfully.</response>
    /// <response code="400">Invalid input or missing required fields.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User is forbidden to execute this workflow.</response>
    /// <response code="404">Workflow or user not found.</response>
    /// <response code="500">Server error while processing the workflow.</response>
    [HttpPost]
    [RequiresCapability(OrganizationCapabilities.RUN)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(WorkflowExecutionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    [SwaggerRequestExample(typeof(StartWorkflowExecutionRequest), typeof(StartWorkflowExecutionRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(BackgroundJobResponseExample))]
    public async Task<IActionResult> StartWorkflowExecution(
        [FromBody] StartWorkflowExecutionRequest request)
    {
        string? username = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value;
        string? email = User.FindFirst(ClaimTypes.Email)?.Value;

        var command = new StartWorkflowExecutionCommand(
               request,
               OrganizationId,
               UserId,
               _currentUser.GetUsername(),
               _currentUser.GetEmail());

        ErrorOr<WorkflowExecutionDto> result = await _sender.Send(command);

        return result.Match(
            reminder => CreatedAtAction(
                actionName: nameof(WorkflowReportsController.GetById),
                controllerName: nameof(WorkflowReportsController).Replace("Controller", string.Empty),
                routeValues: new { id = result.Value.Id },
                value: result.Value),
            Problem);
    }

    /// <summary>
    /// Submits a workflow execution with files and metadata in a single multipart request.
    /// The caller provides all required information (workflow ID, files with document classes,
    /// variable overrides) and the endpoint handles file upload and execution start internally.
    /// </summary>
    /// <param name="idempotencyKey">Optional idempotency key to prevent duplicate submissions on retry.</param>
    /// <returns>The created workflow execution.</returns>
    /// <response code="201">Workflow execution created and queued successfully.</response>
    /// <response code="400">Invalid input, missing metadata, or a file the platform cannot accept: the <c>errors</c> map names each failed file under <c>files[index]</c> with the reason, and every document the request created is deleted again.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="404">Workflow not found.</response>
    [HttpPost("submit")]
    [RequiresCapability(OrganizationCapabilities.RUN)]
    [RequestSizeLimit(LARGE_PAYLOAD_LIMIT_BYTES)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(WorkflowExecutionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        if (!Request.HasFormContentType)
        {
            return Problem([Error.Validation(description: "Request must be multipart/form-data.")]);
        }

        IFormCollection form = await Request.ReadFormAsync(HttpContext.RequestAborted);

        string? metadataJson = form["metadata"];
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Problem([Error.Validation(description: "The 'metadata' form field is required and must contain valid JSON.")]);
        }

        SubmitWorkflowExecutionRequest? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<SubmitWorkflowExecutionRequest>(metadataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            return Problem([Error.Validation(description: $"Invalid JSON in 'metadata' field: {ex.Message}")]);
        }

        if (metadata is null)
        {
            return Problem([Error.Validation(description: "The 'metadata' field deserialized to null.")]);
        }

        IReadOnlyList<IFormFile> files = form.Files.ToList().AsReadOnly();

        var command = new SubmitWorkflowExecutionCommand(
            OrganizationId: OrganizationId,
            UserId: UserId,
            Username: _currentUser.GetUsername(),
            Email: _currentUser.GetEmail(),
            Metadata: metadata,
            Files: files,
            IdempotencyKey: idempotencyKey,
            Submitter: User);

        ErrorOr<WorkflowExecutionDto> result = await _sender.Send(command);

        return result.Match(
            _ => CreatedAtAction(
                actionName: nameof(WorkflowReportsController.GetById),
                controllerName: nameof(WorkflowReportsController).Replace("Controller", string.Empty),
                routeValues: new { id = result.Value.Id },
                value: result.Value),
            Problem);
    }

    /// <summary>
    /// Retrieves a summary list of execution logs for a specific workflow execution.
    /// </summary>
    /// <remarks>
    /// Returns lightweight summary data without prompt/output content for fast initial loading.
    /// Use GET /workflow-executions/{id}/logs/{logId} to retrieve full details including prompt, output, and placeholder values.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>A list of execution log summaries for the specified workflow execution.</returns>
    /// <response code="200">Execution log summaries retrieved successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="404">Workflow execution not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}/logs")]
    [RequiresCapability(OrganizationCapabilities.RUN_LOGS)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<WorkflowExecutionLogSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetExecutionLogs(string id)
    {
        ErrorOr<List<WorkflowExecutionLogSummaryDto>> result = await _sender.Send(
            new ListWorkflowExecutionLogsQuery(OrganizationId, UserId, id));

        return result.Match<IActionResult>(
            value => Ok(new ListResponse<WorkflowExecutionLogSummaryDto>(value)),
            Problem);
    }

    /// <summary>
    /// Retrieves the projected activity digest of an executed Agent block — the paced,
    /// client-facing feed derived from the block's persisted trace pack.
    /// </summary>
    /// <remarks>
    /// Serves the projection only, never raw trace content. A block without a collected
    /// trace (or one outside the caller's organization) answers an empty digest.
    /// </remarks>
    /// <param name="blockId">The unique identifier of the executed Agent block.</param>
    /// <param name="beatSource">Accepted and ignored. Every tool call now earns a beat, so the
    /// narrated/classified split no longer exists; the parameter is kept only so existing
    /// callers do not break.</param>
    /// <param name="round">Optional verification round. A block a verification group redid
    /// bought a run per round, and without this the last-collected one always answers.</param>
    /// <param name="runExecutionId">Optional single run. Takes precedence over the round.</param>
    /// <returns>The block's activity digest.</returns>
    /// <response code="200">Activity digest projected successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("blocks/{blockId}/agent-activity")]
    [RequiresCapability(OrganizationCapabilities.RUN_LOGS)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AgentActivityDigestDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetAgentActivity(
        string blockId,
        [FromQuery] string? beatSource = null,
        [FromQuery] int? round = null,
        [FromQuery] string? runExecutionId = null)
    {
        ErrorOr<AgentActivityDigestDto> result = await _sender.Send(
            new GetAgentRunActivityQuery(OrganizationId, UserId, blockId, beatSource, round, runExecutionId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Lists every collected file of the run's Agent block runs: the trace pack files and the
    /// published artifacts, without their bytes.
    /// </summary>
    /// <remarks>
    /// An operator or super admin reads any run in the organization. The organization's service
    /// user reads the runs it started. Any other caller gets 403, and a run in another
    /// organization answers 404.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>The run's agent run files.</returns>
    /// <response code="200">Files listed.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">The caller may not read this run's evidence.</response>
    /// <response code="404">Workflow execution not found.</response>
    [HttpGet("{id}/agent-runs")]
    [RequiresCapability(OrganizationCapabilities.RUN_EVIDENCE)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<AgentRunFileDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public async Task<IActionResult> ListAgentRunFiles(string id)
    {
        ErrorOr<List<AgentRunFileDto>> result = await _sender.Send(
            new ListExecutionAgentRunFilesQuery(OrganizationId, UserId, id)
            {
                CallerReadsEveryRun = Roles.HeldBy(User, Roles.OPERATOR_ROLES),
            });

        return result.Match<IActionResult>(
            value => Ok(new ListResponse<AgentRunFileDto>(value)),
            Problem);
    }

    /// <summary>
    /// Downloads an Agent block's trace (<c>trace/trace.ndjson</c> from its run pack) as NDJSON.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="runExecutionId"/> the block's most recently collected run answers.
    /// Same access rule as <c>GET {id}/agent-runs</c>.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <param name="blockId">The executed Agent block.</param>
    /// <param name="runExecutionId">Optional: one run of the block, from <c>GET {id}/agent-runs</c>.</param>
    /// <param name="attempt">Optional: one collection attempt of that run.</param>
    /// <returns>The trace file.</returns>
    /// <response code="200">The trace, as NDJSON.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">The caller may not read this run's evidence.</response>
    /// <response code="404">The run, the block or its trace was not found.</response>
    [HttpGet("{id}/agent-runs/{blockId}/trace")]
    [RequiresCapability(OrganizationCapabilities.RUN_EVIDENCE)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public Task<IActionResult> DownloadAgentRunTrace(
        string id,
        string blockId,
        [FromQuery] string? runExecutionId = null,
        [FromQuery] int? attempt = null)
    {
        return DownloadAgentRunFile(id, blockId, AgentRunTraceTokens.TRACE_FILE_PATH, runExecutionId, attempt);
    }

    /// <summary>
    /// Downloads one collected file of an Agent block's run by its path in the run pack, for
    /// example <c>out/result.json</c>.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="runExecutionId"/> the block's most recently collected run answers.
    /// Same access rule as <c>GET {id}/agent-runs</c>.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <param name="blockId">The executed Agent block.</param>
    /// <param name="path">The file's path in the run pack, as <c>GET {id}/agent-runs</c> lists it.</param>
    /// <param name="runExecutionId">Optional: one run of the block.</param>
    /// <param name="attempt">Optional: one collection attempt of that run.</param>
    /// <returns>The file.</returns>
    /// <response code="200">The file.</response>
    /// <response code="400">No path given.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">The caller may not read this run's evidence.</response>
    /// <response code="404">The run, the block or the file was not found.</response>
    [HttpGet("{id}/agent-runs/{blockId}/file")]
    [RequiresCapability(OrganizationCapabilities.RUN_EVIDENCE)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public async Task<IActionResult> DownloadAgentRunFile(
        string id,
        string blockId,
        [FromQuery] string path,
        [FromQuery] string? runExecutionId = null,
        [FromQuery] int? attempt = null)
    {
        ErrorOr<AgentRunFileContent> result = await _sender.Send(
            new GetExecutionAgentRunFileQuery(OrganizationId, UserId, id, blockId, path, runExecutionId, attempt)
            {
                CallerReadsEveryRun = Roles.HeldBy(User, Roles.OPERATOR_ROLES),
            });

        return result.Match<IActionResult>(
            file => File(file.Content, file.ContentType, file.FileName),
            Problem);
    }

    /// <summary>
    /// Retrieves a single workflow execution log by its ID.
    /// </summary>
    /// <remarks>
    /// Returns a single workflow execution log if found.
    /// Otherwise, returns a 404 error.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <param name="logId">The unique identifier of the workflow execution log.</param>
    /// <param name="includePrompt">Whether to include prompt data in the response.</param>
    /// <param name="includeOutput">Whether to include output data in the response.</param>
    /// <returns>The workflow execution log if found.</returns>
    /// <response code="200">Workflow execution log found and returned successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Workflow execution log not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}/logs/{logId}")]
    [RequiresCapability(OrganizationCapabilities.RUN_LOGS)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowOutputExecutionLogDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetExecutionLogById(
        string id,
        string logId,
        [FromQuery] bool includePrompt = true,
        [FromQuery] bool includeOutput = true)
    {
        ErrorOr<WorkflowOutputExecutionLogDto> result = await _sender.Send(
            new GetWorkflowExecutionLogQuery(OrganizationId, UserId, id, logId, includePrompt, includeOutput));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves the full (non-truncated) placeholder values for a specific workflow execution log.
    /// </summary>
    /// <remarks>
    /// Returns the complete placeholder values used in the log's prompt template, without truncation.
    /// This is useful when the standard log endpoint returns truncated values.
    /// Optionally filter by specific placeholder keys using the placeholderKeys query parameter (comma-separated).
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <param name="logId">The unique identifier of the workflow execution log.</param>
    /// <param name="placeholderKeys">Optional comma-separated list of placeholder keys to filter by.</param>
    /// <returns>A dictionary of placeholder values for the specified log.</returns>
    /// <response code="200">Placeholder values retrieved successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Workflow execution log not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}/logs/{logId}/placeholders")]
    [RequiresCapability(OrganizationCapabilities.RUN_LOGS)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Dictionary<string, string>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetExecutionLogPlaceholders(
        string id,
        string logId,
        [FromQuery] string? placeholderKeys = null)
    {
        IEnumerable<string>? keys = null;
        if (!string.IsNullOrWhiteSpace(placeholderKeys))
        {
            keys = placeholderKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k));
        }

        ErrorOr<Dictionary<string, string>> result = await _sender.Send(
            new GetWorkflowExecutionLogPlaceholdersQuery(OrganizationId, UserId, id, logId, keys));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Checks an execution's inputs against its workflow's current template before a re-run.
    /// </summary>
    /// <remarks>
    /// A workflow can change after a run: it can gain a required variable, drop a variable, or
    /// need a document class the run did not have. The response lists the variable values to
    /// submit (the run's values, plus defaults for new variables), the variables and document
    /// classes the user must fill in, and the variables that were dropped or defaulted.
    /// <c>isReady</c> is true when the re-run can start with no input from the user.
    /// Confidential values are never returned.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution to re-run.</param>
    /// <returns>The re-run inputs and what the user must fill in.</returns>
    /// <response code="200">The re-run inputs.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="404">The execution is not found, or its workflow is no longer available to the organization.</response>
    [HttpGet("{id}/rerun-inputs")]
    [RequiresCapability(OrganizationCapabilities.RUN)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReRunInputsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetReRunInputs(string id)
    {
        ErrorOr<ReRunInputsDto> result = await _sender.Send(new GetReRunInputsQuery(OrganizationId, UserId, id));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Transitions a workflow execution to a new state.
    /// </summary>
    /// <remarks>
    /// Currently only <c>Cancelled</c> is supported. Cancellation is allowed only from
    /// <c>Running</c>, <c>ProcessingDocuments</c>, <c>Queued</c>, or <c>Undefined</c>; other states return 400.
    /// If the execution has an active background job, that job is signalled to stop.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <param name="request">The desired target state.</param>
    /// <response code="204">State transitioned successfully.</response>
    /// <response code="400">Target state not supported or current state cannot transition.</response>
    /// <response code="404">Workflow execution not found.</response>
    [HttpPatch("{id}/state")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateState(string id, [FromBody] UpdateWorkflowExecutionStateRequest request)
    {
        var command = new UpdateWorkflowExecutionStateCommand(
            OrganizationId, UserId, id, request.State);
        ErrorOr<Success> result = await _sender.Send(command);
        return result.Match<IActionResult>(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Returns every AI Query block from a persisted execution snapshot with its captured
    /// output and the resolved signature-field values used at execution time. Consumed by
    /// the signature-test-replay playground picker. Prompts are deliberately omitted —
    /// those are Inia IP and never leave the backend.
    /// </summary>
    [HttpGet("{id}/blocks")]
    [RequiresCapability(OrganizationCapabilities.SIGNATURES)]
    [Authorize(Roles = $"{Roles.SUPER_ADMIN},{Roles.SIGNATURE_CONTRIBUTOR}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ExecutionBlocksDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExecutionBlocks(string id)
    {
        var query = new GetExecutionBlocksQuery(OrganizationId, UserId, id);
        ErrorOr<ExecutionBlocksDto> result = await _sender.Send(query);
        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Replays a single AI Query block from a past workflow execution with one signature
    /// value swapped for the user-supplied test value. Calls the AI provider directly with
    /// the modified prompt; no new workflow execution, RAG, or downstream blocks run.
    /// Returns only the resulting signature values + response text + metadata — the modified
    /// prompts themselves stay server-side (Inia IP).
    /// </summary>
    [HttpPost("{id}/test-signature-prompt")]
    [RequiresCapability(OrganizationCapabilities.SIGNATURES)]
    [Authorize(Roles = $"{Roles.SUPER_ADMIN},{Roles.SIGNATURE_CONTRIBUTOR}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SignaturePromptTestResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    public async Task<IActionResult> TestSignaturePrompt(
        string id,
        [FromBody] TestSignaturePromptRequest request)
    {
        var command = new TestSignaturePromptCommand(
            OrganizationId,
            UserId,
            id,
            request.BlockId,
            request.PlaceholderName,
            request.TestValue);
        ErrorOr<SignaturePromptTestResultDto> result = await _sender.Send(command);
        return result.Match<IActionResult>(Ok, Problem);
    }
}
