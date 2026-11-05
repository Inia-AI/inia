using FluentValidation;

namespace IniaAi.Application.Queries.OrganizationModelSettings.GetOrganizationModelSettings;

/// <summary>
/// Validates the <see cref="GetOrganizationModelSettingsQuery"/>.
/// </summary>
public class GetOrganizationModelSettingsQueryValidator : AbstractValidator<GetOrganizationModelSettingsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrganizationModelSettingsQueryValidator"/> class.
    /// </summary>
    public GetOrganizationModelSettingsQueryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);
    }
}
