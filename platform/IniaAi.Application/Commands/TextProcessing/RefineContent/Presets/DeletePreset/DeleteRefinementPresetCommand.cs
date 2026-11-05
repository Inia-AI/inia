using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.DeletePreset;

public record DeleteRefinementPresetCommand(
    string OrganizationId,
    string UserId,
    string Id) : IRequest<ErrorOr<Success>>;
