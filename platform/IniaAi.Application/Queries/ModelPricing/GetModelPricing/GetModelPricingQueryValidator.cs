using FluentValidation;

namespace IniaAi.Application.Queries.ModelPricing.GetModelPricing;

/// <summary>
/// Validates the <see cref="GetModelPricingQuery"/>.
/// </summary>
public class GetModelPricingQueryValidator : AbstractValidator<GetModelPricingQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetModelPricingQueryValidator"/> class.
    /// </summary>
    public GetModelPricingQueryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);
    }
}
