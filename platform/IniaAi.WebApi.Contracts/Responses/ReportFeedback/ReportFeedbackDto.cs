using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.ReportFeedback;

/// <summary>
/// One feedback item on a report, as returned by every feedback endpoint.
/// </summary>
public record ReportFeedbackDto
{
    /// <summary>
    /// Gets the unique identifier of the feedback item.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Gets the execution (report) the feedback belongs to.
    /// </summary>
    public string WorkflowExecutionId { get; init; } = default!;

    /// <summary>
    /// Gets the workflow type the execution ran.
    /// </summary>
    public string? WorkflowTypeId { get; init; }

    /// <summary>
    /// Gets the text the reader had selected in the report, when any.
    /// </summary>
    public string? QuotedText { get; init; }

    /// <summary>
    /// Gets the category of the problem.
    /// </summary>
    public ReportFeedbackCategoryDto Category { get; init; }

    /// <summary>
    /// Gets the reader's comment.
    /// </summary>
    public string Comment { get; init; } = default!;

    /// <summary>
    /// Gets the id of the user who recorded the item.
    /// </summary>
    public string AuthorUserId { get; init; } = default!;

    /// <summary>
    /// Gets the display name of the user who recorded the item; the user id when no name is known.
    /// </summary>
    public string AuthorDisplayName { get; init; } = default!;

    /// <summary>
    /// Gets the lifecycle status.
    /// </summary>
    public ReportFeedbackStatusDto Status { get; init; }

    /// <summary>
    /// Gets the reason the item was dismissed; null unless <see cref="Status"/> is Dismissed.
    /// </summary>
    public string? DismissReason { get; init; }

    /// <summary>
    /// Gets the id of the editor who last set <see cref="Status"/>; null while the item is
    /// still as the reader left it.
    /// </summary>
    public string? StatusChangedByUserId { get; init; }

    /// <summary>
    /// Gets the workflow version whose save carried the workflow agent's fix; null unless that
    /// save marked the item Addressed.
    /// </summary>
    public string? AddressedInVersionId { get; init; }

    /// <summary>
    /// Gets when the item was recorded (UTC).
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets when the item was last changed (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the organization the item belongs to. Populated only on the cross-organization read
    /// by workflow type; null on the org-scoped reads, where it equals the caller's organization.
    /// </summary>
    public string? OrganizationId { get; init; }
}
