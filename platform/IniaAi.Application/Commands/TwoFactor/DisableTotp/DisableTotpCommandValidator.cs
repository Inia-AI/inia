using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.DisableTotp;

/// <summary>
/// Validator for the <see cref="DisableTotpCommand"/>.
/// </summary>
public class DisableTotpCommandValidator : AbstractValidator<DisableTotpCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisableTotpCommandValidator"/> class.
    /// </summary>
    public DisableTotpCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Verification code is required");
    }
}
