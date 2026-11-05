using FluentValidation;

namespace IniaAi.Application.Queries.Organizations.GetUserTenants;

/// <summary>
/// Validator for the <see cref="GetUserTenantsQuery"/> to ensure request parameters are valid.
/// Validates that the user ID is provided and not empty.
/// </summary>
public class GetUserTenantsQueryValidator : AbstractValidator<GetUserTenantsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetUserTenantsQueryValidator"/> class.
    /// Sets up validation rules for the query parameters.
    /// </summary>
    public GetUserTenantsQueryValidator()
    {
        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
