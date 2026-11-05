using FluentValidation;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.DeletePreset;

public class DeleteRefinementPresetCommandValidator : AbstractValidator<DeleteRefinementPresetCommand>
{
    public DeleteRefinementPresetCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Id).NotEmpty();
    }
}
