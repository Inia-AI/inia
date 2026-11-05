using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.ModelPricing;

/// <summary>
/// Represents a pricing component in create/update requests.
/// </summary>
public class ModelPricingComponentRequest
{
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
    /// Default is PerMillion for token-based pricing.
    /// </summary>
    public PricingUnit Unit { get; set; } = PricingUnit.PerMillion;

    /// <summary>
    /// Gets or sets the currency code (ISO 4217). Defaults to USD.
    /// </summary>
    public string Currency { get; set; } = "USD";
}
