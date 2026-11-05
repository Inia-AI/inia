using IniaAi.WebApi.Models.Dto;

namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Represents a logical grouping of workflow types, such as "Asset Management", "Corporate" etc.
/// </summary>
public sealed class WorkflowCategoryDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow category.
    /// </summary>
    /// <example>asset-management</example>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name of the workflow category.
    /// </summary>
    /// <example>Asset Management</example>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the optional description of the workflow category.
    /// </summary>
    /// <example>Workflows for reviews of asset managers and their funds.</example>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of workflows associated with this category.
    /// </summary>
    public List<WorkflowDto>? Workflows { get; set; }
}
