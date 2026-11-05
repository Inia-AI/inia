namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// Represents a single token usage record for OData listing.
/// </summary>
public class TokenUsageDto
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the organization ID.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user ID who triggered the usage.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's display name.
    /// </summary>
    public string? UserDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the workflow type ID.
    /// </summary>
    public string? WorkflowTypeId { get; set; }

    /// <summary>
    /// Gets or sets the workflow type name.
    /// </summary>
    public string? WorkflowTypeName { get; set; }

    /// <summary>
    /// Gets or sets the workflow execution ID.
    /// </summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>
    /// Gets or sets the block ID.
    /// </summary>
    public string? BlockId { get; set; }

    /// <summary>
    /// Gets or sets the document ID.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the usage type (e.g., LlmBlockExecution, EmbeddingPage).
    /// </summary>
    public string UsageType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the execution context (e.g., WorkflowExecution, DocumentUpload).
    /// </summary>
    public string ExecutionContext { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the block type if applicable.
    /// </summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// Gets or sets the AI provider.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model ID.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the usage components (Input, Output, CacheRead, Embedding, etc.).
    /// </summary>
    public List<TokenUsageComponentDto> Components { get; set; } = [];

    /// <summary>
    /// Gets or sets the total cost in USD (sum of all component costs).
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the usage occurred.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of what this token usage was for.
    /// </summary>
    public string? Description { get; set; }
}
