using FluentValidation;

namespace IniaAi.Application.Commands.ModelPricing.DeleteModelPricing;

/// <summary>
/// Validates the <see cref="DeleteModelPricingCommand"/>.
/// </summary>
public class DeleteModelPricingCommandValidator : AbstractValidator<DeleteModelPricingCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteModelPricingCommandValidator"/> class.
    /// </summary>
    public DeleteModelPricingCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);
    }
}
