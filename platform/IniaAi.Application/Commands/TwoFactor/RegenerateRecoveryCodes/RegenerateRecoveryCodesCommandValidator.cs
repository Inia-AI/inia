using FluentValidation;

namespace IniaAi.Application.Commands.TwoFactor.RegenerateRecoveryCodes;

/// <summary>
/// Validator for the <see cref="RegenerateRecoveryCodesCommand"/>.
/// </summary>
public class RegenerateRecoveryCodesCommandValidator : AbstractValidator<RegenerateRecoveryCodesCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegenerateRecoveryCodesCommandValidator"/> class.
    /// </summary>
    public RegenerateRecoveryCodesCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
