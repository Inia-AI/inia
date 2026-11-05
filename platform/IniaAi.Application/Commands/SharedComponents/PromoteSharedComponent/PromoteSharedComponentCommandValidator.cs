using FluentValidation;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;

public class PromoteSharedComponentCommandValidator : AbstractValidator<PromoteSharedComponentCommand>
{
    public PromoteSharedComponentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.TargetPath)
            .Must((command, targetPath) =>
                string.IsNullOrEmpty(targetPath) != string.IsNullOrEmpty(command.TargetStableId))
            .WithMessage($"Provide exactly one of targetPath or targetStableId. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        _ = RuleFor(x => x.TargetPath)
            .Matches(SharedComponentEditor.TARGET_PATH_PATTERN)
            .When(x => !string.IsNullOrEmpty(x.TargetPath));
        _ = RuleFor(x => x.Name).MaximumLength(SharedComponent.NAME_MAX_LENGTH);
        _ = RuleFor(x => x.Name)
            .Empty()
            .When(x => x.AttachToComponentId is not null)
            .WithMessage("Name cannot be combined with attachToComponentId; the existing component keeps its name.");
        _ = RuleFor(x => x.Description)
            .Empty()
            .When(x => x.AttachToComponentId is not null)
            .WithMessage("Description cannot be combined with attachToComponentId; the existing component keeps its description.");
        _ = RuleFor(x => x.CategoryId)
            .NotEmpty()
            .When(x => x.AttachToComponentId is null)
            .WithMessage("CategoryId is required when creating a new component; list the vocabulary to pick one.");
        _ = RuleFor(x => x.CategoryId)
            .Empty()
            .When(x => x.AttachToComponentId is not null)
            .WithMessage("CategoryId cannot be combined with attachToComponentId; the existing component keeps its category.");
    }
}
