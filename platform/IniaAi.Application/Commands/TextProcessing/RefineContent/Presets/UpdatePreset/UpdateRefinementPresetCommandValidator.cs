using FluentValidation;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.UpdatePreset;

public class UpdateRefinementPresetCommandValidator : AbstractValidator<UpdateRefinementPresetCommand>
{
    public UpdateRefinementPresetCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Id).NotEmpty();

        _ = RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        _ = RuleFor(x => x.Instruction)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
