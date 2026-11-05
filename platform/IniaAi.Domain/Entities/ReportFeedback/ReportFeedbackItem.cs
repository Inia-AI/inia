using IniaAi.Common;
using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.ReportFeedback;

/// <summary>
/// One reader's comment on a finished report, kept in UserData beside the execution it concerns.
/// The item belongs to the report as a whole: it is deliberately not keyed on an output element,
/// a block or a stable block id, because blocks are edited, moved and deleted between template
/// versions and data keyed on them has broken before. The execution carries the template version
/// the run used, so nothing here duplicates it. <see cref="QuotedText"/> is the reader's own
/// text selection, kept as plain text, and is the only pointer into the report's content.
/// The first index serves the org-scoped read of one report; the second serves the editor-only
/// cross-organization read by workflow type.
/// </summary>
[Index(nameof(OrganizationId), nameof(WorkflowExecutionId))]
[Index(nameof(WorkflowTypeId), nameof(Status))]
public class ReportFeedbackItem : Entity<ReportFeedbackItem>, IOrganizationScoped, IIdentifiable
{
    public const int MAX_ID_LENGTH = 450;
    public const int MAX_QUOTED_TEXT_LENGTH = 4000;
    public const int MAX_COMMENT_LENGTH = 4000;
    public const int MAX_DISMISS_REASON_LENGTH = 500;

    [Required]
    [StringLength(MAX_ID_LENGTH)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(MAX_ID_LENGTH)]
    public string WorkflowExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// The workflow type the execution ran; null only when the execution itself carries none.
    /// </summary>
    [StringLength(MAX_ID_LENGTH)]
    public string? WorkflowTypeId { get; set; }

    /// <summary>
    /// The text the reader had selected in the report when they left the feedback; null when
    /// the feedback is about the report as a whole.
    /// </summary>
    [StringLength(MAX_QUOTED_TEXT_LENGTH)]
    public string? QuotedText { get; set; }

    public ReportFeedbackCategory Category { get; set; }

    [Required]
    [StringLength(MAX_COMMENT_LENGTH)]
    public string Comment { get; set; } = string.Empty;

    [Required]
    [StringLength(MAX_ID_LENGTH)]
    public string AuthorUserId { get; set; } = string.Empty;

    public ReportFeedbackStatus Status { get; set; } = ReportFeedbackStatus.Open;

    /// <summary>
    /// Why the item was dismissed; set together with <see cref="ReportFeedbackStatus.Dismissed"/>
    /// and cleared on any other status.
    /// </summary>
    [StringLength(MAX_DISMISS_REASON_LENGTH)]
    public string? DismissReason { get; set; }

    /// <summary>
    /// The editor who last moved the item to its current <see cref="Status"/>; null while the
    /// item is still as the reader left it.
    /// </summary>
    [StringLength(MAX_ID_LENGTH)]
    public string? StatusChangedByUserId { get; set; }

    /// <summary>
    /// The workflow version whose save carried the editor agent's fix for this item; null unless
    /// the save marked it <see cref="ReportFeedbackStatus.Addressed"/>, and cleared by any later
    /// status change.
    /// </summary>
    [StringLength(MAX_ID_LENGTH)]
    public string? AddressedInVersionId { get; set; }

    /// <summary>
    /// The one way an item is built: every free-text part is cut to its column width here.
    /// </summary>
    public static ReportFeedbackItem Create(
        string organizationId,
        string workflowExecutionId,
        string? workflowTypeId,
        string? quotedText,
        ReportFeedbackCategory category,
        string comment,
        string authorUserId)
    {
        return new ReportFeedbackItem
        {
            OrganizationId = organizationId,
            WorkflowExecutionId = workflowExecutionId,
            WorkflowTypeId = workflowTypeId,
            QuotedText = quotedText.Truncate(MAX_QUOTED_TEXT_LENGTH),
            Category = category,
            Comment = comment.Truncate(MAX_COMMENT_LENGTH) ?? string.Empty,
            AuthorUserId = authorUserId,
            Status = ReportFeedbackStatus.Open,
        };
    }

    /// <summary>
    /// Moves the item to <paramref name="status"/>. The dismiss reason is kept only while the
    /// item is dismissed, so a re-opened item does not carry a stale reason.
    /// </summary>
    public void SetStatus(ReportFeedbackStatus status, string? dismissReason, string changedByUserId)
    {
        Status = status;
        DismissReason = status == ReportFeedbackStatus.Dismissed
            ? dismissReason.Truncate(MAX_DISMISS_REASON_LENGTH)
            : null;
        StatusChangedByUserId = changedByUserId;
        AddressedInVersionId = null;
        UpdatedByUserId = changedByUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A save of <paramref name="versionId"/> carried a fix for the item.</summary>
    public void MarkAddressed(string versionId, string changedByUserId)
    {
        SetStatus(ReportFeedbackStatus.Addressed, dismissReason: null, changedByUserId);
        AddressedInVersionId = versionId;
    }
}
