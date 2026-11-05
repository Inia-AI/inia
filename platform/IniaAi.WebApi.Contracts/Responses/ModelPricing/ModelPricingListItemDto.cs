using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.ModelPricing;

/// <summary>
/// Represents a summary item for listing model pricings.
/// </summary>
public class ModelPricingListItemDto
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AI provider.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model family (e.g., "Anthropic", "Cohere").
    /// </summary>
    public string? Family { get; set; }

    /// <summary>
    /// Gets or sets the model identifier.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model alias for matching.
    /// </summary>
    public string? ModelAlias { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pricing type.
    /// </summary>
    public PricingType PricingType { get; set; }

    /// <summary>
    /// Gets or sets whether this pricing is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date from which this pricing is effective.
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// Gets or sets the cost per million input tokens (for LLM).
    /// </summary>
    public decimal? InputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Gets or sets the cost per million output tokens (for LLM).
    /// </summary>
    public decimal? OutputTokenCostPerMillion { get; set; }

    /// <summary>
    /// Gets or sets the cost per million embedding tokens (for Embedding).
    /// </summary>
    public decimal? EmbeddingCostPerMillion { get; set; }
}
