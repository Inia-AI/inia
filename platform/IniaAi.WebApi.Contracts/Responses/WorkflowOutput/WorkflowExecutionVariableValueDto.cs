namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a workflow variable's resolved display value for a specific execution.
/// </summary>
public record WorkflowExecutionVariableValueDto
{
    /// <summary>
    /// The variable's replacement tag (key) from the workflow definition.
    /// </summary>
    public string ReplacementTag { get; init; } = default!;

    /// <summary>
    /// The human-readable display name from the workflow definition.
    /// </summary>
    public string DisplayName { get; init; } = default!;

    /// <summary>
    /// Optional description from the workflow definition. Null when not supplied.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Variable type from <c>IniaAi.Domain.Entities.Workflows.Enums.WorkflowVariableTypes</c>, serialized as string.
    /// </summary>
    public string Type { get; init; } = default!;

    /// <summary>
    /// Server-resolved display value. Null when no value was supplied.
    /// Confidential variables with a value present arrive masked.
    /// </summary>
    public string? DisplayValue { get; init; }

    /// <summary>
    /// Whether the variable is flagged confidential by the workflow definition.
    /// </summary>
    public bool IsConfidential { get; init; }

    /// <summary>
    /// Whether the variable is flagged hidden by the workflow definition. A run's read leaves hidden variables
    /// out, so a row never carries true; the field stays for clients that read it.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Whether the variable is required by the workflow definition.
    /// </summary>
    public bool IsRequired { get; init; }
}
