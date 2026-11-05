using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.RemoveTrustedDevice;

/// <summary>
/// Validator for the <see cref="RemoveTrustedDeviceCommand"/>.
/// </summary>
public class RemoveTrustedDeviceCommandValidator : AbstractValidator<RemoveTrustedDeviceCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTrustedDeviceCommandValidator"/> class.
    /// </summary>
    public RemoveTrustedDeviceCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithMessage("Device ID is required");
    }
}
