namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Lightweight DTO for workflow with minimal info (no Variables, DocumentClasses, etc.).
/// Used for listing workflows where full details are not needed.
/// </summary>
public sealed class WorkflowSummaryDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the workflow type.
    /// </summary>
    public required string WorkflowTypeId { get; set; }

    /// <summary>
    /// Gets or sets the name of the workflow type.
    /// </summary>
    public string? WorkflowTypeName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow is available.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// The credits a run of this workflow type costs; null when a run of it costs none, which is
    /// always the case where the deployment charges no credits.
    /// </summary>
    public int? CreditCost { get; set; }

    /// <summary>
    /// The pages a run of this workflow type may read, as the platform counts them at upload; null
    /// when a run of it has no page limit, which is always the case where the deployment sets none.
    /// </summary>
    public int? MaxPages { get; set; }
}
