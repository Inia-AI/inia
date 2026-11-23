using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Stores pricing information for AI models (LLM, embedding, vision, audio).
/// Pricing components are stored in a normalized child table (ModelPricingComponent)
/// for flexibility across different pricing structures and currencies.
/// </summary>
public class ModelPricing : Entity<ModelPricing>
{
    /// <summary>
    /// The AI provider identifier: ModelIdNormalizer.GetProvider() output for a model call, and
    /// the connector's Provider for an embeddings call.
    /// Examples: "anthropic-api", "aws-bedrock", "cohere-api".
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The model family (e.g., "Anthropic", "Cohere").
    /// Used for filtering and grouping models.
    /// </summary>
    public string? Family { get; set; }

    /// <summary>
    /// The specific model identifier (e.g., "claude-sonnet-4-5-20250929").
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Model alias for matching (e.g., "claude-sonnet-4-5").
    /// </summary>
    public string? ModelAlias { get; set; }

    /// <summary>
    /// Human-readable display name (e.g., "Claude Sonnet 4.5 (Direct API)").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The type of model pricing (LLM, Embedding, Vision, Audio).
    /// </summary>
    public PricingType PricingType { get; set; }

    /// <summary>
    /// Date from which this pricing is effective.
    /// </summary>
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date until which this pricing is effective (null = no expiration).
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// Whether this pricing record is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The prompt length above which a request pays the <c>LongPrompt*</c> components instead of
    /// the standard ones (Claude Haiku 5.5: 100,000). A prompt counts its input, cache read and
    /// cache write tokens. Null when the model has one price for every prompt length.
    /// </summary>
    public int? LongPromptThresholdTokens { get; set; }

    /// <summary>
    /// What a call through a regional endpoint costs as a multiple of the components: 1.10 on
    /// the AWS Bedrock rows of Claude models from the 4.5 generation on, which Bedrock bills 10%
    /// above the global endpoint for a geo (<c>us.</c>, <c>eu.</c>) or in-region profile. A call
    /// through a <c>global.</c> profile pays the components as they are. Null when the row has
    /// one price for every endpoint.
    /// </summary>
    [Precision(6, 4)]
    public decimal? RegionalEndpointMultiplier { get; set; }

    /// <summary>
    /// Navigation property to pricing components (Input, Output, Cache, etc.).
    /// </summary>
    public ICollection<ModelPricingComponent> Components { get; set; } = [];
}
