using FluentValidation;
using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Application.Commands.ModelPricing.UpdateModelPricing;

/// <summary>
/// Validates the <see cref="UpdateModelPricingCommand"/>.
/// </summary>
public class UpdateModelPricingCommandValidator : AbstractValidator<UpdateModelPricingCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateModelPricingCommandValidator"/> class.
    /// </summary>
    public UpdateModelPricingCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(36);

        _ = RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("Provider is required.")
            .MaximumLength(100);

        _ = RuleFor(x => x.ModelId)
            .NotEmpty().WithMessage("ModelId is required.")
            .MaximumLength(200);

        _ = RuleFor(x => x.ModelAlias)
            .MaximumLength(200);

        _ = RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("DisplayName is required.")
            .MaximumLength(250);

        _ = RuleFor(x => x.PricingType)
            .IsInEnum().WithMessage("PricingType must be a valid value.");

        _ = RuleFor(x => x.LongPromptThresholdTokens)
            .GreaterThanOrEqualTo(0).WithMessage("LongPromptThresholdTokens cannot be negative.");

        _ = RuleFor(x => x.Components)
            .NotNull().WithMessage("Components list is required.");

        // Validate each component
        _ = RuleForEach(x => x.Components).ChildRules(component =>
        {
            _ = component.RuleFor(c => c.ComponentType)
                .IsInEnum().WithMessage("ComponentType must be a valid value.");

            _ = component.RuleFor(c => c.Cost)
                .GreaterThanOrEqualTo(0).WithMessage("Cost must be non-negative.");

            _ = component.RuleFor(c => c.Unit)
                .IsInEnum().WithMessage("Unit must be a valid value.");

            _ = component.RuleFor(c => c.Currency)
                .NotEmpty().WithMessage("Currency is required.")
                .Length(3).WithMessage("Currency must be a 3-letter ISO code.");
        });

        // Business rule: LLM pricing should have Input and Output components
        _ = RuleFor(x => x)
            .Must(x => x.PricingType is not PricingType.Llm ||
                (x.Components.Any(c => c.ComponentType is PricingComponentType.Input) &&
                 x.Components.Any(c => c.ComponentType is PricingComponentType.Output)))
            .WithMessage("LLM pricing must have Input and Output components.");

        // Business rule: Embedding pricing should have Embedding component
        _ = RuleFor(x => x)
            .Must(x => x.PricingType is not PricingType.Embedding ||
                x.Components.Any(c => c.ComponentType is PricingComponentType.Embedding))
            .WithMessage("Embedding pricing must have an Embedding component.");
    }
}
