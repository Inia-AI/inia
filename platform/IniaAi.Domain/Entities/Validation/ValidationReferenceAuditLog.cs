using IniaAi.Common;
using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// Append-only history of reference-cache value transitions on a validation case
/// (extractions, confirmations, corrections). <see cref="CaseId"/> is a loose id — no
/// foreign key — so the trail survives re-extraction rebuilds; the actor email is
/// snapshotted so attribution survives user renames and deletions.
/// <see cref="Entity{T}.CreatedAt"/> is the event timestamp; rows are never updated.
/// The second index serves the agreement rate, which counts one action across a window
/// for a whole organization rather than reading one case.
/// </summary>
[Index(nameof(OrganizationId), nameof(CaseId), nameof(CreatedAt))]
[Index(nameof(OrganizationId), nameof(Action), nameof(CreatedAt))]
public class ValidationReferenceAuditLog : Entity<ValidationReferenceAuditLog>, IOrganizationScoped, IIdentifiable
{
    public const int MAX_NAME_LENGTH = 200;
    public const int MAX_ROW_KEY_LENGTH = 450;
    public const int MAX_VALUE_LENGTH = 4000;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string CaseId { get; set; } = string.Empty;

    /// <summary>
    /// The group of the value the entry concerns; null for a top-level scalar field.
    /// </summary>
    [StringLength(MAX_NAME_LENGTH)]
    public string? GroupName { get; set; }

    /// <summary>
    /// The display key of the row the value sits in; null for a top-level scalar field.
    /// </summary>
    [StringLength(MAX_ROW_KEY_LENGTH)]
    public string? RowKey { get; set; }

    [Required]
    [StringLength(MAX_NAME_LENGTH)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The case's <see cref="ValidationCase.ReferenceRevision"/> this change produced.
    /// </summary>
    public int Revision { get; set; }

    [Required]
    [StringLength(32)]
    public string Action { get; set; } = string.Empty;

    [StringLength(MAX_VALUE_LENGTH)]
    public string? PreviousValue { get; set; }

    [StringLength(MAX_VALUE_LENGTH)]
    public string? NewValue { get; set; }

    [StringLength(450)]
    public string? ActorUserId { get; set; }

    [StringLength(256)]
    public string? ActorUserEmail { get; set; }

    /// <summary>
    /// The one way an entry is built: every free-text part is cut to its column width here,
    /// so no writer has to know the widths.
    /// </summary>
    public static ValidationReferenceAuditLog Record(
        string organizationId,
        string caseId,
        int revision,
        string action,
        string? groupName,
        string? rowKey,
        string fieldName,
        string? previousValue,
        string? newValue,
        string? actorUserId = null,
        string? actorUserEmail = null)
    {
        return new ValidationReferenceAuditLog
        {
            OrganizationId = organizationId,
            CaseId = caseId,
            Revision = revision,
            Action = action,
            GroupName = groupName.Truncate(MAX_NAME_LENGTH),
            RowKey = rowKey.Truncate(MAX_ROW_KEY_LENGTH),
            FieldName = fieldName.Truncate(MAX_NAME_LENGTH),
            PreviousValue = previousValue.Truncate(MAX_VALUE_LENGTH),
            NewValue = newValue.Truncate(MAX_VALUE_LENGTH),
            ActorUserId = actorUserId,
            ActorUserEmail = actorUserEmail,
        };
    }
}
