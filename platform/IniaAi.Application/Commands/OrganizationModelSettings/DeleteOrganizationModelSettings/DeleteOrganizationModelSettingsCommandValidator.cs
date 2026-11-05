using FluentValidation;

namespace IniaAi.Application.Commands.OrganizationModelSettings.DeleteOrganizationModelSettings;

/// <summary>
/// Validates the <see cref="DeleteOrganizationModelSettingsCommand"/>.
/// </summary>
public class DeleteOrganizationModelSettingsCommandValidator : AbstractValidator<DeleteOrganizationModelSettingsCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteOrganizationModelSettingsCommandValidator"/> class.
    /// </summary>
    public DeleteOrganizationModelSettingsCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);
    }
}
