using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows.Templates;

/// <summary>
/// A category of the retired Prompt Library, kept with its table until a later release drops both.
/// </summary>
public class TemplateCategory : Entity<TemplateCategory>
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ParentCategoryId { get; set; }

    [Required]
    public int DisplayOrder { get; set; } = 0;

    public string? CreatedByUserId { get; set; }

    // Navigation property
    public TemplateCategory? ParentCategory { get; set; }
}
