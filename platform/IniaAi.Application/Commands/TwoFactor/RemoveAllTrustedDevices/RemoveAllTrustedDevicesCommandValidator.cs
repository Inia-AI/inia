using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.RemoveAllTrustedDevices;

/// <summary>
/// Validator for the <see cref="RemoveAllTrustedDevicesCommand"/>.
/// </summary>
public class RemoveAllTrustedDevicesCommandValidator : AbstractValidator<RemoveAllTrustedDevicesCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveAllTrustedDevicesCommandValidator"/> class.
    /// </summary>
    public RemoveAllTrustedDevicesCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
