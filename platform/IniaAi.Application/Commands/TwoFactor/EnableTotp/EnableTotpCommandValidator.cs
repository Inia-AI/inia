using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.EnableTotp;

/// <summary>
/// Validator for the <see cref="EnableTotpCommand"/>.
/// </summary>
public class EnableTotpCommandValidator : AbstractValidator<EnableTotpCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnableTotpCommandValidator"/> class.
    /// </summary>
    public EnableTotpCommandValidator()
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
