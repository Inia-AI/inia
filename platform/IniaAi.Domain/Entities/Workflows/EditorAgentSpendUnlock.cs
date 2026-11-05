using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// More daily batches of workflow agent spend, unlocked by a user for their organization after
/// the warning that today's batches were used. Unique on the batches the day had before, so two
/// users unlocking at the same moment add them once.
/// </summary>
[Index(nameof(OrganizationId), nameof(Day), nameof(FromBatches), IsUnique = true)]
public class EditorAgentSpendUnlock
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>The UTC day the batches add to.</summary>
    public DateOnly Day { get; set; }

    /// <summary>How many batches the day had when the user unlocked.</summary>
    public int FromBatches { get; set; }

    /// <summary>How many batches this unlock added: enough to bring the day one batch above its spend.</summary>
    public int Batches { get; set; }

    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
}
