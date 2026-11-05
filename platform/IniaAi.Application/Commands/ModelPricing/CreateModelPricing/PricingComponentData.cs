using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Application.Commands.ModelPricing.CreateModelPricing;

/// <summary>
/// Represents a pricing component in commands.
/// </summary>
/// <param name="ComponentType">The type of pricing component (Input, Output, Cache, Embedding, etc.).</param>
/// <param name="Cost">The cost value for this component.</param>
/// <param name="Unit">The unit of measurement (PerMillion, PerUnit, etc.).</param>
/// <param name="Currency">The currency code (ISO 4217). Defaults to USD.</param>
public record PricingComponentData(
    PricingComponentType ComponentType,
    decimal Cost,
    PricingUnit Unit = PricingUnit.PerMillion,
    string Currency = "USD");
