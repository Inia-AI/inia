using FluentValidation;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.SharedComponents.MaterializeSharedComponent;

public class MaterializeSharedComponentCommandValidator : AbstractValidator<MaterializeSharedComponentCommand>
{
    public MaterializeSharedComponentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.TargetSectionPath)
            .Must((command, targetSectionPath) =>
                string.IsNullOrEmpty(targetSectionPath) || string.IsNullOrEmpty(command.TargetSectionStableId))
            .WithMessage($"Provide at most one of targetSectionPath or targetSectionStableId. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        _ = RuleFor(x => x.TargetSectionPath)
            .Matches(SharedComponentEditor.SECTION_PATH_PATTERN)
            .When(x => !string.IsNullOrEmpty(x.TargetSectionPath));
        _ = RuleFor(x => x.Index)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Index.HasValue);
    }
}
