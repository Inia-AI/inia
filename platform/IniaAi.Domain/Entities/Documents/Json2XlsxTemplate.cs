using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Associates a FileMetadata (XLSX template file) with an organization and conversion type.
/// Used by the Json2Xlsx workflow block to load pre-existing client templates.
/// </summary>
public class Json2XlsxTemplate : Entity<Json2XlsxTemplate>
{
    [Required]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The conversion type display name.
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
    /// The user who originally uploaded this template.
    /// </summary>
    public string? CreatedByUserId { get; set; }
}
