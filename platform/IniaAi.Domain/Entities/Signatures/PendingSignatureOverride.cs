using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// A client-proposed change to a workflow-level signature field that is waiting for
/// reviewer votes. Exactly one pending may exist per
/// <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c>. A new submission while
/// one exists replaces it and clears all votes. When approvals ≥ tenant threshold the
/// <see cref="ProposedValue"/> is promoted to a <see cref="SignatureFieldOverride"/>
/// row and this row is deleted; when rejections ≥ threshold the row is deleted with no
/// change to the active override.
/// </summary>
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(PlaceholderName), IsUnique = true)]
[Index(nameof(OrganizationId), nameof(WorkflowTypeId))]
public class PendingSignatureOverride : Entity<PendingSignatureOverride>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string PlaceholderName { get; set; } = string.Empty;

    /// <summary>
    /// The proposed value. <see langword="null"/> means the proposer is asking to
    /// remove the current active override and fall back to the staff-authored default.
    /// </summary>
    [StringLength(4000)]
    public string? ProposedValue { get; set; }

    [Required]
    [StringLength(450)]
    public string ProposerUserId { get; set; } = string.Empty;

    public ICollection<SignatureOverrideVote> Votes { get; private set; } = [];
}
