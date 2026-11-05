using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows.Templates;

/// <summary>
/// A prompt of the retired Prompt Library. Only the startup migration that moves these prompts
/// into workflows and Tools Settings reads it; the table goes in a later release.
/// </summary>
public class BlockTemplate : Entity<BlockTemplate>
{
    public const int CODE_MAX_LENGTH = 100;
    public const int NAME_MAX_LENGTH = 200;
    public const int DESCRIPTION_MAX_LENGTH = 1000;

    [Required]
    [StringLength(CODE_MAX_LENGTH)]
    public string TemplateCode { get; set; } = string.Empty;

    [Required]
    public BlockTypes BlockType { get; set; }

    public string? OrganizationId { get; set; }

    [Required]
    public TemplateScope TemplateScope { get; set; }

    [Required]
    public string CategoryId { get; set; } = string.Empty;

    [StringLength(450)]
    public string? WorkflowTypeId { get; set; }

    public string? DocumentCategoryId { get; set; }

    /// <summary>
    /// BlockId of the parent template this was derived from.
    /// NULL for base templates (e.g., page-overviews-general).
    /// Set for variations (e.g., page-overviews-hf-monitoring points to page-overviews-general).
    /// Used to track template inheritance and show "based on" relationships in UI.
    /// </summary>
    public string? ParentTemplateId { get; set; }

    [Required]
    public int TemplateVersion { get; set; } = 1;

    [Required]
    public int TemplateUsageCount { get; set; } = 0;

    [Required]
    public bool IsSystemTemplate { get; set; } = false;

    [Required]
    public bool IsPublicTemplate { get; set; } = false;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;

    // Navigation properties
    public Block? Block { get; set; }
    public TemplateCategory? TemplateCategory { get; set; }
    public DocumentCategory? DocumentCategory { get; set; }
    public BlockTemplate? ParentTemplate { get; set; }
}
