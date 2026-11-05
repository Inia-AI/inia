using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.ModelPricing;

/// <summary>
/// Represents full details of a model pricing entry.
/// </summary>
public class ModelPricingDto
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AI provider (e.g., "anthropic-api", "aws-bedrock").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model family (e.g., "Anthropic", "Cohere").
    /// </summary>
    public string? Family { get; set; }

    /// <summary>
    /// Gets or sets the specific model identifier.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model alias for matching.
    /// </summary>
    public string? ModelAlias { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of pricing (Llm or Embedding).
    /// </summary>
    public PricingType PricingType { get; set; }

    /// <summary>
    /// Gets or sets the date from which this pricing is effective.
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// Gets or sets the date until which this pricing is effective.
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// Gets or sets whether this pricing record is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the prompt length above which a request pays the long-prompt components, or
    /// null when the model has one price for every prompt length.
    /// </summary>
    public int? LongPromptThresholdTokens { get; set; }

    /// <summary>
    /// What a call through a regional endpoint costs as a multiple of the components: 1.10 on
    /// the AWS Bedrock rows of Claude models from the 4.5 generation on, for a geo (<c>us.</c>,
    /// <c>eu.</c>) or in-region profile. A <c>global.</c> profile pays the components. Null when
    /// the row has one price for every endpoint. Seeded; the update form does not change it.
    /// </summary>
    public decimal? RegionalEndpointMultiplier { get; set; }

    /// <summary>
    /// Gets or sets the pricing components (Input, Output, Cache, Embedding, etc.).
    /// </summary>
    public List<ModelPricingComponentDto> Components { get; set; } = [];
}
