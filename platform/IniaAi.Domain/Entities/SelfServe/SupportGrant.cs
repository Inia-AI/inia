using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// A customer's permission for support to read one report, or the organization's documents and
/// reports when no report is named, until it expires or ends.
/// </summary>
[Index(nameof(OrganizationId), nameof(ExpiresAt))]
[Index(nameof(HelpRequestId))]
public class SupportGrant
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [StringLength(450)]
    public string? WorkflowExecutionId { get; set; }

    [StringLength(450)]
    public string? HelpRequestId { get; set; }

    [Required]
    [StringLength(450)]
    public string GrantedByUserId { get; set; } = string.Empty;

    /// <summary>The operator the grant lets in.</summary>
    [StringLength(450)]
    public string? OperatorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? EndedAt { get; set; }

    [StringLength(450)]
    public string? EndedByUserId { get; set; }

    public ICollection<SupportAccess> Accesses { get; set; } = [];
}
