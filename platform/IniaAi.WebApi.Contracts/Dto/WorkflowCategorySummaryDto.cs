namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Lightweight DTO for workflow category with minimal workflow info.
/// Used for initial page load where full workflow details are not needed.
/// </summary>
public sealed class WorkflowCategorySummaryDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow category.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name of the workflow category.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the optional description of the workflow category.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the display order for sorting categories.
    /// </summary>
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets the list of workflow summaries in this category.
    /// </summary>
    public List<WorkflowSummaryDto>? Workflows { get; set; }
}
