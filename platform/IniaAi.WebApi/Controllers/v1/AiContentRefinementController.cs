using ErrorOr;
using IniaAi.Application.Commands.TextProcessing.RefineContent;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Models;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.CreatePreset;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.DeletePreset;
using IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.UpdatePreset;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.TextProcessing.RefineContent.GetPresets;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.Models.Requests.ContentRefinement;
using IniaAi.WebApi.Models.Responses.ContentRefinement;
using IniaAi.WebApi.Models.Responses.DailyAllowances;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides AI-powered content refinement services for iterative content improvement.
/// </summary>
[Route("api/v{version:apiVersion}/ai/content-refinement")]
[RequiresCapability(OrganizationCapabilities.REFINE)]
public sealed class AiContentRefinementController : ApiController
{
    private readonly ISender _sender;
    private readonly ILogger<AiContentRefinementController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiContentRefinementController"/> class.
    /// </summary>
    /// <param name="requestContext"></param>
    /// <param name="sender"></param>
    /// <param name="logger"></param>
    public AiContentRefinementController(
        IRequestContext requestContext,
        ISender sender,
        ILogger<AiContentRefinementController> logger)
        : base(requestContext)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Refines the input content based on the provided instruction.
    /// Supports iterative refinement through conversation history.
    /// </summary>
    /// <param name="request">The refinement request containing content, instruction, and optional conversation history.</param>
    /// <returns>The refined content and an explanation of changes.</returns>
    /// <response code="429">The user has made every refine request the day allows; the body says when they reset.</response>
    [HttpPost]
    [DailyAllowance(DailyAllowanceKind.Refine)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RefineContentResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(DailyAllowanceRefusal))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<RefineContentResponse>> Refine([FromBody] RefineContentRequest request)
    {
        var command = new RefineContentCommand(
            OrganizationId,
            UserId,
            request.Content,
            request.Instruction,
            request.ConversationHistory
                .Select(t => new RefinementTurnDto(t.Role, t.Content))
                .ToList());

        ErrorOr<RefineContentResult> result = await _sender.Send(command);

        _logger.LogInformation(
            "RefineContentCommand completed with result: {Result}",
            result.IsError
                ? string.Join(", ", result.Errors.Select(e => e.Description))
                : "Success");

        return result.Match<ActionResult<RefineContentResponse>>(
            refinementResult => Ok(new RefineContentResponse
            {
                RefinedContent = refinementResult.RefinedContent,
                Explanation = refinementResult.Explanation,
                IsOutOfScope = refinementResult.IsOutOfScope,
            }),
            errors => errors.Any(e => e.Code == "ContentRefinement.UpstreamEmpty")
                ? Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "The upstream refinement service returned an empty response.")
                : Problem(errors));
    }

    /// <summary>
    /// Retrieves the list of content refinement presets available to the user.
    /// </summary>
    /// <returns></returns>
    [HttpGet("presets")]
    public async Task<ActionResult<List<RefinementPresetResponse>>> GetPresets()
    {
        var query = new GetRefinementPresetsQuery(OrganizationId, UserId);
        ErrorOr<List<RefinementPresetResponse>> result = await _sender.Send(query);
        return result.Match(Ok, Problem);
    }

    /// <summary>
    /// Creates a new content refinement preset for the user.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost("presets")]
    public async Task<ActionResult<RefinementPresetResponse>> CreatePreset(
        [FromBody] CreateRefinementPresetRequest request)
    {
        var command = new CreateRefinementPresetCommand(
            OrganizationId,
            UserId,
            request.Name,
            request.Instruction,
            request.IsShared);
        ErrorOr<RefinementPresetResponse> result = await _sender.Send(command);
        return result.Match(Ok, Problem);
    }

    /// <summary>
    /// Updates an existing content refinement preset. Only the owner of a preset can update it.
    /// This applies regardless of whether the preset is shared (visible to others) or admin status.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPut("presets/{id}")]
    public async Task<ActionResult<RefinementPresetResponse>> UpdatePreset(
        string id,
        [FromBody] UpdateRefinementPresetRequest request)
    {
        var command = new UpdateRefinementPresetCommand(
            OrganizationId,
            UserId,
            id,
            request.Name,
            request.Instruction,
            request.IsShared);
        ErrorOr<RefinementPresetResponse> result = await _sender.Send(command);
        return result.Match(Ok, Problem);
    }

    /// <summary>
    /// Deletes a content refinement preset. Only the owner can delete their presets.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpDelete("presets/{id}")]
    public async Task<IActionResult> DeletePreset(string id)
    {
        var command = new DeleteRefinementPresetCommand(OrganizationId, UserId, id);
        ErrorOr<Success> result = await _sender.Send(command);
        return result.Match(_ => NoContent(), Problem);
    }
}
