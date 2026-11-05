using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.ModelPricing;

/// <summary>
/// Represents a pricing component for a model.
/// </summary>
public class ModelPricingComponentDto
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of pricing component (Input, Output, Cache, Embedding, etc.).
    /// </summary>
    public PricingComponentType ComponentType { get; set; }

    /// <summary>
    /// Gets or sets the cost value for this component.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Gets or sets the unit of measurement (PerMillion, PerUnit, etc.).
    /// </summary>
    public PricingUnit Unit { get; set; }

    /// <summary>
    /// Gets or sets the currency code (ISO 4217). Defaults to USD.
    /// </summary>
    public string Currency { get; set; } = "USD";
}
