using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>One read an operator made under a support grant.</summary>
[Index(nameof(SupportGrantId), nameof(ReadAt))]
public class SupportAccess
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string SupportGrantId { get; set; } = string.Empty;

    [ForeignKey(nameof(SupportGrantId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public SupportGrant SupportGrant { get; set; } = null!;

    [Required]
    [StringLength(450)]
    public string OperatorUserId { get; set; } = string.Empty;

    /// <summary>What the operator read, such as a report or a document.</summary>
    [Required]
    [StringLength(500)]
    public string Resource { get; set; } = string.Empty;

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
