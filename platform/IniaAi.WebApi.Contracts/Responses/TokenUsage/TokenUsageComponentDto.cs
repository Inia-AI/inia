using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// Represents a token usage component with its cost.
/// </summary>
public class TokenUsageComponentDto
{
    /// <summary>
    /// Gets or sets the component type (Input, Output, CacheRead, etc.).
    /// </summary>
    public PricingComponentType ComponentType { get; set; }

    /// <summary>
    /// Gets or sets the number of units consumed (tokens, images, seconds, etc.).
    /// </summary>
    public int Units { get; set; }

    /// <summary>
    /// Gets or sets the cost for this component in USD.
    /// </summary>
    public decimal Cost { get; set; }
}
