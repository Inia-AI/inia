using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Represents a single usage component for a TokenUsage record.
/// This normalized structure allows flexible tracking across different
/// component types (input, output, cache, embedding, reasoning, image, etc.)
/// and mirrors the ModelPricingComponent structure for easy cost calculation.
/// </summary>
public class TokenUsageComponent : Entity<TokenUsageComponent>
{
    /// <summary>
    /// Foreign key to the parent TokenUsage record.
    /// </summary>
    public string TokenUsageId { get; set; } = string.Empty;

    /// <summary>
    /// The type of usage component (Input, Output, CacheRead, Embedding, Reasoning, etc.).
    /// Uses the same enum as ModelPricingComponent for direct cost lookup.
    /// </summary>
    public PricingComponentType ComponentType { get; set; }

    /// <summary>
    /// The number of units consumed (tokens, images, seconds, etc.).
    /// The interpretation depends on ComponentType and the associated PricingUnit.
    /// </summary>
    public int Units { get; set; }

    /// <summary>
    /// The calculated cost for this component in USD.
    /// Computed by matching ComponentType to ModelPricingComponent.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Navigation property to the parent TokenUsage.
    /// </summary>
    public TokenUsage TokenUsage { get; set; } = null!;
}
