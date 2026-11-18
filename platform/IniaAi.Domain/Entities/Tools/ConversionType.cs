using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Tools;

/// <summary>
/// A JSON-to-Excel conversion type: the name under which an organization stores the
/// template that a Json2Xlsx block fills.
/// </summary>
public class ConversionType : Entity<ConversionType>
{
    /// <summary>
    /// Internal identifier used in block configuration and template lookup.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-facing display label shown in dropdowns and management UI.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The organization this conversion type belongs to.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;
}
