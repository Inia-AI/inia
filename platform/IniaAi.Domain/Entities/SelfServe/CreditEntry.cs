using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// One append-only line of the credit ledger. Every change to the account's available credits
/// writes one, with the balance before and after. A hold's <see cref="Status"/> is the only value
/// that ever changes, once, from pending to posted or cancelled; a posted hold is the charge.
/// </summary>
/// <remarks>
/// The organization, user, run and payment event are plain columns with no foreign key, so
/// deleting any of them never deletes ledger history.
/// </remarks>
[Index(nameof(OrganizationId), nameof(CreatedAt))]
[Index(nameof(OrganizationId), nameof(Status))]
[Index(nameof(Status), nameof(OrganizationId))]
[Index(nameof(WorkflowExecutionId))]
[Index(nameof(PaymentEventId))]
public class CreditEntry
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string CreditPackId { get; set; } = string.Empty;

    [ForeignKey(nameof(CreditPackId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public CreditPack CreditPack { get; set; } = null!;

    public CreditEntryKind Kind { get; set; }

    /// <summary>The signed change to the account's available credits.</summary>
    public int Amount { get; set; }

    public int AvailableBefore { get; set; }

    public int AvailableAfter { get; set; }

    [StringLength(450)]
    public string? WorkflowExecutionId { get; set; }

    [StringLength(450)]
    public string? PaymentEventId { get; set; }

    /// <summary>The hold a release or an expiry of held credits settles.</summary>
    [StringLength(450)]
    public string? HoldEntryId { get; set; }

    [ForeignKey(nameof(HoldEntryId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public CreditEntry? HoldEntry { get; set; }

    /// <summary>Set on holds only.</summary>
    public CreditHoldStatus? Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string? CreatedByUserId { get; set; }
}
