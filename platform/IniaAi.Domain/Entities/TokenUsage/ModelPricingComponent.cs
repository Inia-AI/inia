using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Represents a single pricing component for an AI model.
/// This normalized structure allows flexible pricing across different
/// component types (input, output, cache, embedding, image, audio, etc.)
/// with support for multiple currencies and unit types.
/// </summary>
public class ModelPricingComponent : Entity<ModelPricingComponent>
{
    /// <summary>
    /// Foreign key to the parent ModelPricing record.
    /// </summary>
    public string ModelPricingId { get; set; } = string.Empty;

    /// <summary>
    /// The type of pricing component (Input, Output, CacheWrite5m, Embedding, Image, etc.).
    /// </summary>
    public PricingComponentType ComponentType { get; set; }

    /// <summary>
    /// The cost value for this component.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// The unit of measurement for the cost (PerMillion, PerUnit, PerMinute, etc.).
    /// </summary>
    public PricingUnit Unit { get; set; } = PricingUnit.PerMillion;

    /// <summary>
    /// The currency code (ISO 4217) for the cost. Defaults to USD.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Navigation property to the parent ModelPricing.
    /// </summary>
    public ModelPricing ModelPricing { get; set; } = null!;
}
