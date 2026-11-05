using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// An immutable point-in-time snapshot of a Core workflow template, appended on every save.
/// Carries the canonical content hash, a pointer to the version it was edited from, and an
/// auto-generated plain-language summary of what changed. History is append-only: versions are
/// never hard-deleted (retention only trims the heavy payload), so the self-referencing parent
/// pointer uses ON DELETE NO ACTION — also the only self-FK delete behavior SQL Server permits
/// (SET NULL or CASCADE on a self-reference fail with error 1785). Versions form one canonical
/// chain per workflow type. Rows created before the chain-merge migration (which collapsed the
/// original per-organization chains) had <see cref="ParentVersionId"/> rewritten to chronological
/// order — for those rows the pointer is NOT edit provenance and must never be read as such.
/// </summary>
[Index(nameof(WorkflowTypeId), nameof(CreatedAt))]
[Index(nameof(ContentHash))]
public class WorkflowVersion : Entity<WorkflowVersion>
{
    [Required]
    [StringLength(450)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [StringLength(450)]
    public string? ParentVersionId { get; set; }

    [ForeignKey(nameof(ParentVersionId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public WorkflowVersion? ParentVersion { get; set; }

    public byte[]? WorkflowJsonCompressed { get; set; }

    /// <summary>
    /// Set when retention trims this version's heavy payload to reclaim storage. It is the sole
    /// signal that the content is gone: a null <see cref="WorkflowJsonCompressed"/> alone cannot be
    /// told apart from a legitimately empty snapshot, so readers must check this timestamp, not the
    /// payload length, to know whether a version is still diffable. The metadata (hash, lineage,
    /// summary) is always retained, so trimming never affects comparison or provenance.
    /// </summary>
    public DateTime? PayloadTrimmedAt { get; set; }

    [NotMapped]
    public string WorkflowJson
    {
        get => WorkflowJsonCompression.Decompress(WorkflowJsonCompressed);
        set => WorkflowJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    public string? AutoNarrativeSummary { get; set; }

    /// <summary>
    /// The version's cached activity script (see <c>WorkflowActivityScriptSerializer</c>):
    /// plain-language step lines a running execution's activity surface resolves against, written
    /// once per version by a background cheap-model pass and immutable afterwards. Null until
    /// first generated; keyed to the version row itself, so an edited template — a new version —
    /// can never serve a stale script. Metadata like the summary: retention never trims it.
    /// </summary>
    public string? ActivityScriptJson { get; set; }

    public string? HumanNote { get; set; }

    [Required]
    [StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
}
