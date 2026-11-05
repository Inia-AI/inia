using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// Append-only history of value transitions on <see cref="SignatureFieldOverride"/>.
/// One row per change emitted by <c>SignatureOverrideWriter.ApplyUpdatesAsync</c> so
/// users can see what an override used to be and who put it forward, even though the
/// pending review and vote rows are deleted when a proposal resolves.
///
/// <para>
/// Each row is associated with the same
/// <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c> tuple the live override
/// uses. The tuple is not unique on this table: rows are append-only, so multiple
/// history entries per field are expected. The primary key is <see cref="Entity{T}.Id"/>.
/// Rows are frozen snapshots and are never re-keyed: <see cref="BlockId"/> is null on
/// rows written after signature fields moved to workflow level, holds the block's
/// <c>StableBlockId</c> on rows written after the stable-id re-key, and the original
/// block entity id on rows written before it. Post-hoist rows instead carry
/// <see cref="ReferencedBlockNamesJson"/>, a snapshot of the block names whose
/// templates referenced the field at write time.
/// </para>
///
/// <para>
/// <see cref="Entity{T}.CreatedAt"/> is the canonical event timestamp; rows are never
/// updated so a separate occurred-at column would be redundant.
/// </para>
/// </summary>
// Single index keyed for the workflow-scoped, newest-first History query.
// A previous `(OrganizationId, BlockId, PlaceholderName)` index was dropped
// because no read path used it — the live `SignatureFieldOverride` owns the
// per-field lookup pattern via that triple, not the audit log. Add it back
// only if a per-field history-drill-down endpoint materialises.
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(CreatedAt))]
public class SignatureFieldOverrideAuditLog : Entity<SignatureFieldOverrideAuditLog>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    // Length matches `PendingSignatureOverride.WorkflowTypeId` so cross-table
    // joins and indexes share an identical key width. The previous 250 here
    // produced a 50-byte schema asymmetry that EF Core would silently widen on
    // SQL Server but that an index re-keying would need to honour eventually.
    [Required]
    [StringLength(200)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Legacy block association, frozen: null on rows written after signature fields
    /// moved to workflow level. See the class remarks for the id-space history.
    /// </summary>
    [StringLength(450)]
    public string? BlockId { get; set; }

    [Required]
    [StringLength(200)]
    public string PlaceholderName { get; set; } = string.Empty;

    /// <summary>
    /// One of <see cref="SignatureAuditAction.CREATED"/>,
    /// <see cref="SignatureAuditAction.UPDATED"/>, or
    /// <see cref="SignatureAuditAction.REMOVED"/>.
    /// </summary>
    [Required]
    [StringLength(32)]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Override value before the change. Null on <see cref="SignatureAuditAction.CREATED"/>.
    /// </summary>
    [StringLength(4000)]
    public string? PreviousValue { get; set; }

    /// <summary>
    /// Override value after the change. Null on <see cref="SignatureAuditAction.REMOVED"/>.
    /// </summary>
    [StringLength(4000)]
    public string? NewValue { get; set; }

    /// <summary>
    /// The proposer of the pending whose resolution caused this change. Nullable for
    /// backfill rows and defensive system writes; in practice the writer is always
    /// called with the proposer's id.
    /// </summary>
    [StringLength(450)]
    public string? ActorUserId { get; set; }

    /// <summary>
    /// Actor email snapshotted at write time so attribution survives user renames or
    /// deletions. Null when the actor cannot be resolved (e.g. backfill rows for
    /// overrides whose <c>UpdatedByUserId</c> is no longer in the users table).
    /// </summary>
    [StringLength(256)]
    public string? ActorUserEmail { get; set; }

    /// <summary>
    /// JSON-serialized snapshot of the approving voters at promotion time. Shape:
    /// <c>[{"UserId":"...","Email":"..."}, ...]</c> (PascalCase keys, matching the
    /// writer's pinned serializer options). Null for backfill rows; an empty array
    /// <c>"[]"</c> when the writer is called with no approvers.
    /// </summary>
    [StringLength(4000)]
    public string? ApproversJson { get; set; }

    /// <summary>
    /// JSON-serialized snapshot of the names of the blocks whose templates referenced
    /// this field when the row was written. Shape: <c>["Block A","Block B"]</c>. Lets
    /// history render block context accurately even after later template restructures.
    /// Null on rows written before signature fields moved to workflow level.
    /// </summary>
    [StringLength(4000)]
    public string? ReferencedBlockNamesJson { get; set; }
}
