using FluentValidation;

namespace IniaAi.Application.Queries.TwoFactor.GetTrustedDevices;

/// <summary>
/// Validator for the <see cref="GetTrustedDevicesQuery"/>.
/// </summary>
public class GetTrustedDevicesQueryValidator : AbstractValidator<GetTrustedDevicesQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetTrustedDevicesQueryValidator"/> class.
    /// </summary>
    public GetTrustedDevicesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
