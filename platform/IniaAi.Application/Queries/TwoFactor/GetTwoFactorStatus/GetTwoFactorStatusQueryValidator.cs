using FluentValidation;

namespace IniaAi.Application.Queries.TwoFactor.GetTwoFactorStatus;

/// <summary>
/// Validator for the <see cref="GetTwoFactorStatusQuery"/>.
/// </summary>
public class GetTwoFactorStatusQueryValidator : AbstractValidator<GetTwoFactorStatusQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetTwoFactorStatusQueryValidator"/> class.
    /// </summary>
    public GetTwoFactorStatusQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
