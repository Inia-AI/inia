using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// An archived, superseded version of a <see cref="Json2XlsxTemplate"/>. When a template is
/// re-uploaded the previous upload is captured here (file bytes are retained in storage) so users
/// can view and download earlier versions. Versions are append-only history, keyed to a logical
/// template by <see cref="OrganizationId"/> + <see cref="ConversionType"/>; they are never the row
/// the workflow runtime resolves, so the current-template lookup is unaffected.
/// </summary>
[Index(nameof(OrganizationId), nameof(ConversionType))]
public class Json2XlsxTemplateVersion : Entity<Json2XlsxTemplateVersion>
{
    [Required]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The conversion type display name this version belonged to.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string ConversionType { get; set; } = string.Empty;

    [Required]
    public string FileMetadataId { get; set; } = string.Empty;

    [ForeignKey(nameof(FileMetadataId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public FileMetadata? FileMetadata { get; set; }

    [Required]
    [StringLength(450)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// The user who originally uploaded this (now superseded) version. Copied from the head row at
    /// archive time so it reflects the original uploader, not whoever triggered the replacement.
    /// </summary>
    public string? UploadedByUserId { get; set; }

    /// <summary>
    /// When this version was originally uploaded. Copied from the head row's creation time at archive
    /// time, so it is the original upload moment rather than the archive moment.
    /// </summary>
    public DateTime UploadedAt { get; set; }
}
