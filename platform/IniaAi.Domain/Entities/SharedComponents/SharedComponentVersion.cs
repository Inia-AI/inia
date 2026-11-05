using IniaAi.Domain.Common;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.SharedComponents;

/// <summary>
/// Append-only version chain of a shared component's definition content. Rows are never deleted —
/// soft or hard — and never retention-trimmed: the head payload IS the definition (no relational
/// copy exists). <see cref="ContentJsonCompressed"/> may be null only on hash-only rows created by
/// a shallow transfer import; <see cref="PayloadAbsentAt"/> marks those (readers must check it, not
/// payload length, since decompressing null yields an empty string) and the head row is never
/// payload-absent. The head is the row with the highest <see cref="SequenceNumber"/> — CreatedAt is
/// client-generated and batch imports can tie on it, so it is never an ordering key. The unique
/// (SharedComponentId, SequenceNumber) index enforces chain linearity and makes concurrent appends
/// fail cleanly; <see cref="ParentVersionId"/> is the explicit lineage edge. ContentHash is
/// deliberately non-unique: content-identical components and revert rows legally repeat a hash.
/// </summary>
[Index(nameof(SharedComponentId), nameof(SequenceNumber), IsUnique = true)]
[Index(nameof(ContentHash))]
public class SharedComponentVersion : Entity<SharedComponentVersion>
{
    [Required]
    [StringLength(450)]
    public string SharedComponentId { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    [Required]
    [StringLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [StringLength(450)]
    public string? ParentVersionId { get; set; }

    [ForeignKey(nameof(ParentVersionId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public SharedComponentVersion? ParentVersion { get; set; }

    public byte[]? ContentJsonCompressed { get; set; }

    public DateTime? PayloadAbsentAt { get; set; }

    [NotMapped]
    public string ContentJson
    {
        get => WorkflowJsonCompression.Decompress(ContentJsonCompressed);
        set => ContentJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    [Required]
    [StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
}
