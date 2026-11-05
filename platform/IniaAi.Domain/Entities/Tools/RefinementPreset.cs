using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Tools;

/// <summary>
/// A saved refinement instruction preset that users can reuse across sessions.
/// Can be personal (user-specific) or shared (organization-wide).
/// </summary>
[Index(nameof(OrganizationId), nameof(IsDeleted), nameof(UserId))]
public class RefinementPreset : Entity<RefinementPreset>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// When true, the preset is visible to all users in the organization.
    /// When false, only the creating user can see and use it.
    /// </summary>
    public bool IsShared { get; set; } = false;
}
