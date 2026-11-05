using FluentValidation;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.CreatePreset;

public class CreateRefinementPresetCommandValidator : AbstractValidator<CreateRefinementPresetCommand>
{
    public CreateRefinementPresetCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();

        _ = RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Preset name is required.")
            .MaximumLength(100)
            .WithMessage("Preset name cannot exceed 100 characters.");

        _ = RuleFor(x => x.Instruction)
            .NotEmpty()
            .WithMessage("Instruction is required.")
            .MaximumLength(2000)
            .WithMessage("Instruction cannot exceed 2000 characters.");
    }
}
