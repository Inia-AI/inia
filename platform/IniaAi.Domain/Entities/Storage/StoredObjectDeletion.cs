using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Storage;

/// <summary>
/// A stored file whose rows are gone but whose delete from its store failed, kept until a retry
/// deletes it. Nothing else points at the file any more, so this row is the only way back to it.
/// </summary>
[Index(nameof(NextAttemptAt))]
public class StoredObjectDeletion
{
    [Key]
    [StringLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>The store's name, as a file's row records it: Database, S3 or Minio.</summary>
    [StringLength(50)]
    public string StorageProvider { get; set; } = string.Empty;

    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [StringLength(450)]
    public string StorageKey { get; set; } = string.Empty;

    public int Attempts { get; set; }

    /// <summary>What the last failed attempt answered, shortened; a store's error names no content.</summary>
    [StringLength(1000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
}
