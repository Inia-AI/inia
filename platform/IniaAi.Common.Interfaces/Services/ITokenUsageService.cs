using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Enums;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Common.Interfaces.Services;

/// <summary>
/// Represents a single usage component for tracking.
/// Mirrors PricingComponentType for direct cost calculation.
/// </summary>
public record UsageComponent
{
    /// <summary>
    /// The type of usage component (Input, Output, CacheRead, Embedding, Reasoning, etc.).
    /// </summary>
    public required PricingComponentType ComponentType { get; init; }

    /// <summary>
    /// The number of units consumed (tokens, images, seconds, etc.).
    /// </summary>
    public required int Units { get; init; }

    /// <summary>
    /// What this component cost, when the producer already knows. Set by producers whose one
    /// record sums several model requests, so a per-request price (a long-prompt tier) cannot be
    /// read from the record: an agent run prices its requests itself (<c>AgentRunBilling</c>).
    /// Null leaves the cost to be derived from the model's pricing row, the record read as one
    /// request.
    /// </summary>
    public decimal? CostUsd { get; init; }
}

/// <summary>
/// Service for recording token usage and calculating costs.
/// </summary>
public interface ITokenUsageService
{
    /// <summary>
    /// Records token usage from an LLM call and returns the cost it recorded, in USD — zero
    /// when no pricing row matched or the record could not be written.
    /// </summary>
    Task<decimal> RecordLlmUsageAsync(
        LlmUsageRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The active LLM pricing row for a provider and model, matched on model id then alias, or
    /// null when none matches or the lookup failed. For callers that price requests themselves
    /// because one record sums several of them (an agent run).
    /// </summary>
    Task<ModelPricing?> FindLlmPricingAsync(
        string provider,
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records token usage from an embedding operation.
    /// </summary>
    Task RecordEmbeddingUsageAsync(
        EmbeddingUsageRecord record,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Record containing LLM token usage data.
/// </summary>
public record LlmUsageRecord
{
    // Required dimensional keys
    public required string OrganizationId { get; init; }

    // Optional dimensional keys
    public string? UserId { get; init; }
    public string? WorkflowTypeId { get; init; }
    public string? WorkflowExecutionId { get; init; }
    public string? SectionId { get; init; }
    public string? BlockId { get; init; }
    public string? DocumentId { get; init; }
    public string? PageId { get; init; }

    // Type classifications
    public required UsageType UsageType { get; init; }
    public required TokenExecutionContext ExecutionContext { get; init; }
    public BlockTypes? BlockType { get; init; }

    // Provider, family, and model
    /// <summary>
    /// The gateway/API endpoint used (e.g., "aws-bedrock", "anthropic-api").
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The model vendor/family (e.g., "anthropic").
    /// </summary>
    public required string Family { get; init; }

    /// <summary>
    /// The specific model identifier (e.g., "claude-sonnet-4-5-20250929").
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Usage components with units consumed per component type.
    /// </summary>
    public required IReadOnlyList<UsageComponent> Components { get; init; }

    /// <summary>
    /// Human-readable description of what this token usage was for.
    /// E.g., "AI Query block 'Market Analysis' in Document Review workflow"
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional JSON metadata for additional context (e.g., RAG selected page IDs).
    /// </summary>
    public string? Metadata { get; init; }

    /// <summary>
    /// True when the record is exactly one model request, so a price that depends on prompt
    /// length (a long-prompt tier) is read from its own token counts. False, the default, for a
    /// record that may sum several requests (an accumulator's total): such a record is priced at
    /// the standard rates, because a sum over the threshold says nothing about any one request.
    /// </summary>
    public bool IsSingleRequest { get; init; }

    /// <summary>
    /// The model id the request was sent to, before normalization: a Bedrock inference profile
    /// such as <c>us.anthropic.claude-sonnet-5-5</c>. Bedrock bills a regional profile above a
    /// global one (<see cref="ModelPricingRates.EndpointMultiplierFor"/>). Null when the producer
    /// does not know it; the service then reads the stack's configured route.
    /// </summary>
    public string? EndpointModelId { get; init; }
}

/// <summary>
/// Record containing embedding token usage data.
/// </summary>
public record EmbeddingUsageRecord
{
    // Required dimensional keys
    public required string OrganizationId { get; init; }

    // Optional dimensional keys
    public string? UserId { get; init; }
    public string? WorkflowTypeId { get; init; }
    public string? WorkflowExecutionId { get; init; }
    public string? SectionId { get; init; }
    public string? BlockId { get; init; }
    public string? DocumentId { get; init; }
    public string? PageId { get; init; }

    // Type classifications
    public required UsageType UsageType { get; init; }
    public required TokenExecutionContext ExecutionContext { get; init; }

    /// <summary>
    /// The type of block that triggered this embedding operation (for RAG query embeddings).
    /// </summary>
    public BlockTypes? BlockType { get; init; }

    // Provider, family, and model
    /// <summary>
    /// The gateway/API endpoint used (e.g., "aws-bedrock").
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The model vendor/family (e.g., "anthropic").
    /// </summary>
    public required string Family { get; init; }

    /// <summary>
    /// The specific model identifier (e.g., "text-embedding-3-large").
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Usage components with units consumed per component type.
    /// </summary>
    public required IReadOnlyList<UsageComponent> Components { get; init; }

    /// <summary>
    /// Human-readable description of what this token usage was for.
    /// E.g., "RAG embedding query for 'Financial Analysis' block"
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional JSON metadata for additional context.
    /// </summary>
    public string? Metadata { get; init; }
}
