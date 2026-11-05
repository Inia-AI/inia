using IniaAi.AiConnection.Common;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.Graph;
using IniaAi.WebApi.Contracts.Responses.Graph;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.Models.Responses.DailyAllowances;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints for querying workflow execution graphs and provenance.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-executions/{runId}/graph")]
[RequiresCapability(OrganizationCapabilities.EXPLORE)]
public sealed class WorkflowExecutionsGraphController : ApiController
{
    private const string RUN_NOT_FOUND = "Workflow execution not found.";

    private readonly ILogger<WorkflowExecutionsGraphController> _logger;
    private readonly IGraphQueryService _graphQueryService;
    private readonly ITokenUsageService _tokenUsageService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowExecutionsGraphController"/> class.
    /// </summary>
    public WorkflowExecutionsGraphController(
        IRequestContext requestContext,
        ILogger<WorkflowExecutionsGraphController> logger,
        IGraphQueryService graphQueryService,
        ITokenUsageService tokenUsageService)
        : base(requestContext)
    {
        _logger = logger;
        _graphQueryService = graphQueryService;
        _tokenUsageService = tokenUsageService;
    }

    /// <summary>
    /// Validates that a target tag exists in the workflow execution graph.
    /// </summary>
    /// <remarks>
    /// Lightweight check before running an expensive graph query. Returns 200 if target exists,
    /// 404 if not found. Use this to provide early feedback to users before querying.
    /// </remarks>
    /// <param name="runId">The workflow execution run ID</param>
    /// <param name="targetTag">The ReplacementTag to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>200 if target exists, 404 otherwise</returns>
    /// <response code="200">Target tag exists in the graph</response>
    /// <response code="401">Unauthorized access</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">Run or target tag not found, or the run belongs to another organization.</response>
    [HttpGet("validate/{targetTag}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateTarget(
        [FromRoute] string runId,
        [FromRoute] string targetTag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OrganizationId))
            {
                _logger.LogWarning("Validation attempt without organization context: runId={RunId}", LogSanitizer.ForLog(runId));
                return Unauthorized(new { error = "Organization context is missing" });
            }

            // Validate access to the workflow execution
            bool hasAccess = await _graphQueryService.ValidateAccessAsync(runId, OrganizationId, cancellationToken);
            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Run not found in the caller's organization: OrganizationId={OrganizationId} asked for runId={RunId}",
                    LogSanitizer.ForLog(OrganizationId),
                    LogSanitizer.ForLog(runId));
                return NotFound(new { error = RUN_NOT_FOUND });
            }

            bool exists = await _graphQueryService.ValidateTargetExistsAsync(runId, targetTag, cancellationToken);

            if (!exists)
            {
                _logger.LogWarning(
                    "Validation failed: targetTag={TargetTag} not found in runId={RunId}",
                    LogSanitizer.ForLog(targetTag),
                    LogSanitizer.ForLog(runId));

                return NotFound(new { error = $"Target tag '{targetTag}' not found in workflow execution '{runId}'" });
            }

            _logger.LogInformation(
                "Validation successful: targetTag={TargetTag} exists in runId={RunId}",
                LogSanitizer.ForLog(targetTag),
                LogSanitizer.ForLog(runId));

            return Ok(new { exists = true, targetTag, runId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during validation: runId={RunId}, targetTag={TargetTag}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(targetTag));

            return StatusCode(500, new { error = "An error occurred while validating the target tag" });
        }
    }

    /// <summary>
    /// Queries the workflow execution graph for upstream provenance of a specific field/block.
    /// </summary>
    /// <remarks>
    /// Performs BFS traversal to find all upstream dependencies (blocks, variables, documents)
    /// that contributed to the target block's output. Returns provenance data including nodes,
    /// edges, paths, and optional output previews.
    /// </remarks>
    /// <param name="runId">The workflow execution run ID</param>
    /// <param name="request">The query request containing target tag and question</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Provenance result with answer, citations, and graph metadata</returns>
    /// <response code="200">Provenance computed successfully</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="401">Unauthorized access</response>
    /// <response code="403">The request names no organization the caller belongs to.</response>
    /// <response code="404">Run or target tag not found, or the run belongs to another organization.</response>
    /// <response code="422">No upstream path exists for target</response>
    /// <response code="429">The user has asked every explore question the day allows; the body says when they reset.</response>
    [HttpPost("query")]
    [RequestTimeout(60000)]
    [DailyAllowance(DailyAllowanceKind.Explore)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GraphQueryResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(DailyAllowanceRefusal))]
    public async Task<IActionResult> QueryProvenance(
        [FromRoute] string runId,
        [FromBody] GraphQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OrganizationId))
            {
                _logger.LogWarning("Query attempt without organization context: runId={RunId}", LogSanitizer.ForLog(runId));
                return Unauthorized(new { error = "Organization context is missing" });
            }

            bool hasAccess = await _graphQueryService.ValidateAccessAsync(runId, OrganizationId, cancellationToken);

            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Run not found in the caller's organization: OrganizationId={OrganizationId} queried runId={RunId}",
                    LogSanitizer.ForLog(OrganizationId),
                    LogSanitizer.ForLog(runId));
                return NotFound(new { error = RUN_NOT_FOUND });
            }

            _logger.LogInformation(
                "Graph query requested for runId={RunId}, targetTag={TargetTag}, startingDepth={StartingDepth}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(request.TargetTag),
                request.StartingDepth);

            // Reset token accumulator before the operation
            if (_graphQueryService is ITokenAccumulator tokenAccumulator)
            {
                tokenAccumulator.ResetTokenAccumulator();
            }

            // Call the service
            ProvenanceResult result = await _graphQueryService.QueryAsync(
                runId,
                request.TargetTag,
                request.Question,
                request.StartingDepth,
                request.IncludePreviews,
                cancellationToken);

            // Record token usage (fire-and-forget)
            RecordGraphQueryTokenUsage(runId);

            var response = new GraphQueryResponse { Answer = result.Answer };

            _logger.LogInformation(
                "Graph query completed for runId={RunId}, targetTag={TargetTag}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(request.TargetTag));

            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            _logger.LogWarning(ex,
                "Graph query failed: runId={RunId}, targetTag={TargetTag}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(request.TargetTag));

            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No upstream"))
        {
            _logger.LogWarning(ex,
                "No provenance path found: runId={RunId}, targetTag={TargetTag}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(request.TargetTag));

            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during graph query: runId={RunId}, targetTag={TargetTag}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(request.TargetTag));

            return StatusCode(500, new { error = "An error occurred while processing the graph query" });
        }
    }

    /// <summary>
    /// Records token usage from the graph query operation using fire-and-forget pattern.
    /// </summary>
    private void RecordGraphQueryTokenUsage(string runId)
    {
        if (_graphQueryService is not ITokenAccumulator tokenAccumulator)
        {
            return;
        }

        int inputTokens = tokenAccumulator.AccumulatedInputTokens;
        int outputTokens = tokenAccumulator.AccumulatedOutputTokens;

        if (inputTokens == 0 && outputTokens == 0)
        {
            return;
        }

        string modelId = tokenAccumulator.LastUsedModelId ?? "graph-query";
        var record = new LlmUsageRecord
        {
            OrganizationId = OrganizationId,
            UserId = UserId,
            WorkflowExecutionId = runId,
            UsageType = UsageType.LlmGraphQuery,
            ExecutionContext = TokenExecutionContext.ApiCall,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(inputTokens, outputTokens),
            Description = $"Graph provenance query for execution '{runId}'"
        };

        // Fire-and-forget - don't await to avoid blocking the response
        _ = _tokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
    }
}
