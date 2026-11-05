using FluentValidation;

namespace IniaAi.Application.Commands.OrganizationModelSettings.UpdateOrganizationModelSettings;

/// <summary>
/// Validates the <see cref="UpdateOrganizationModelSettingsCommand"/>.
/// </summary>
public class UpdateOrganizationModelSettingsCommandValidator : AbstractValidator<UpdateOrganizationModelSettingsCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateOrganizationModelSettingsCommandValidator"/> class.
    /// </summary>
    public UpdateOrganizationModelSettingsCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);

        _ = RuleFor(x => x.OrganizationId)
            .MaximumLength(36)
            .When(x => x.OrganizationId is not null);

        _ = RuleFor(x => x.UsageContext)
            .IsInEnum().WithMessage("UsageContext must be a valid value.");

        _ = RuleFor(x => x.ModelPricingId)
            .NotEmpty().WithMessage("ModelPricingId is required.")
            .MaximumLength(36);

        _ = RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("Priority must be non-negative.");
    }
}
