using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Append-only record of a password-protected document being decrypted at ingest.
/// One row per unlock, written in the same transaction as the document itself.
/// Deliberately carries no foreign key so the trail survives document deletion;
/// <see cref="Entity{T}.CreatedAt"/> is the event timestamp. The password is never stored.
/// </summary>
[Index(nameof(OrganizationId), nameof(CreatedAt))]
public class DocumentUnlockAuditLog : Entity<DocumentUnlockAuditLog>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string DocumentId { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string DocumentName { get; set; } = string.Empty;

    [StringLength(450)]
    public string? ActorUserId { get; set; }

    /// <summary>
    /// Actor email snapshotted at write time so attribution survives user renames or deletions.
    /// </summary>
    [StringLength(256)]
    public string? ActorUserEmail { get; set; }

    /// <summary>
    /// True when the uploader supplied a password; false for owner-only PDFs stripped without a prompt.
    /// </summary>
    public bool PasswordSupplied { get; set; }

    /// <summary>
    /// True when the decrypted PDF contained a digital signature (invalidated by the strip and disclosed to the uploader).
    /// </summary>
    public bool ContainedSignature { get; set; }

    public static DocumentUnlockAuditLog For(
        Document document,
        bool containedSignature,
        bool passwordSupplied,
        string? actorUserId,
        string? actorUserEmail)
    {
        return new DocumentUnlockAuditLog
        {
            OrganizationId = document.OrganizationId,
            DocumentId = document.Id,
            DocumentName = document.Name,
            ActorUserId = actorUserId,
            ActorUserEmail = actorUserEmail,
            PasswordSupplied = passwordSupplied,
            ContainedSignature = containedSignature,
        };
    }
}
