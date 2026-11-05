using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.ModelPricing;

/// <summary>
/// Represents the request payload to create a new model pricing entry.
/// </summary>
public class CreateModelPricingRequest
{
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
    /// Gets or sets the model alias for matching (optional).
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
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date until which this pricing is effective.
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// Gets or sets whether this pricing record is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the pricing components (Input, Output, Cache, Embedding, etc.).
    /// </summary>
    public List<ModelPricingComponentRequest> Components { get; set; } = [];

    /// <summary>
    /// Gets or sets the prompt length (input, cache read and cache write tokens) above which a
    /// request pays the <c>LongPrompt*</c> components. Null for a model with one price for every
    /// prompt length.
    /// </summary>
    public int? LongPromptThresholdTokens { get; set; }
}
