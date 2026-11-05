using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.SetupTotp;

/// <summary>
/// Validator for the <see cref="SetupTotpCommand"/>.
/// </summary>
public class SetupTotpCommandValidator : AbstractValidator<SetupTotpCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetupTotpCommandValidator"/> class.
    /// </summary>
    public SetupTotpCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
