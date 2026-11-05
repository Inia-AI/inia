using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Models.Responses.ContentRefinement;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.UpdatePreset;

public record UpdateRefinementPresetCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string Name,
    string Instruction,
    bool IsShared) : IRequest<ErrorOr<RefinementPresetResponse>>;
