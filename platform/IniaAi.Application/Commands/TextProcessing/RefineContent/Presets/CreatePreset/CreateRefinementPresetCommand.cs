using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Models.Responses.ContentRefinement;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.CreatePreset;

public record CreateRefinementPresetCommand(
    string OrganizationId,
    string UserId,
    string Name,
    string Instruction,
    bool IsShared) : IRequest<ErrorOr<RefinementPresetResponse>>;
