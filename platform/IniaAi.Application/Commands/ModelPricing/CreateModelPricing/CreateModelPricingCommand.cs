using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;

namespace IniaAi.Application.Commands.ModelPricing.CreateModelPricing;

/// <summary>
/// Command to create a new model pricing entry.
/// </summary>
/// <param name="Provider">The AI provider (e.g., "anthropic-api", "aws-bedrock").</param>
/// <param name="ModelId">The specific model identifier.</param>
/// <param name="ModelAlias">Model alias for matching (optional).</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="PricingType">The type of pricing (LLM or Embedding).</param>
/// <param name="EffectiveFrom">Date from which this pricing is effective.</param>
/// <param name="EffectiveTo">Date until which this pricing is effective.</param>
/// <param name="IsActive">Whether this pricing record is currently active.</param>
/// <param name="Components">The pricing components (Input, Output, Cache, Embedding, etc.).</param>
/// <param name="Family">The model family (e.g., "Anthropic"), or null when the request names none.</param>
/// <param name="LongPromptThresholdTokens">The prompt length above which a request pays the long-prompt components, or null for none.</param>
public record CreateModelPricingCommand(
    string Provider,
    string ModelId,
    string? ModelAlias,
    string DisplayName,
    PricingType PricingType,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    List<PricingComponentData> Components,
    string? Family = null,
    int? LongPromptThresholdTokens = null) : IRequest<ErrorOr<ModelPricingDto>>;
