using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// One batch of credits with its own use-by date. A paid invoice grants at most one pack: the
/// provider and its invoice id are unique together.
/// </summary>
[Index(nameof(OrganizationId), nameof(ExpiresAt))]
[Index(nameof(PaymentProvider), nameof(ProviderInvoiceId), IsUnique = true)]
public class CreditPack
{
    public const int PAYMENT_PROVIDER_MAX_LENGTH = 50;
    public const int PROVIDER_ID_MAX_LENGTH = 255;
    public const int REASON_MAX_LENGTH = 500;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    public CreditPackSource Source { get; set; }

    public int Amount { get; set; }

    /// <summary>Credits of this pack that no run holds or has spent, after any debt it repaid.</summary>
    public int Remaining { get; set; }

    /// <summary>Empty for a pack that never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    [StringLength(PAYMENT_PROVIDER_MAX_LENGTH)]
    public string? PaymentProvider { get; set; }

    [StringLength(PROVIDER_ID_MAX_LENGTH)]
    public string? ProviderInvoiceId { get; set; }

    /// <summary>The invoice's amount paid, in the currency's minor unit.</summary>
    public long? InvoiceAmountPaid { get; set; }

    [StringLength(3)]
    public string? InvoiceCurrency { get; set; }

    /// <summary>Credits taken back by refunds so far, so a repeated refund event never reverses twice.</summary>
    public int Reversed { get; set; }

    /// <summary>Credits of this pack that paid the account's debt: when it was granted, or when a run or a dispute gave them back.</summary>
    public int DebtRepaid { get; set; }

    [StringLength(450)]
    public string? GrantedByUserId { get; set; }

    [StringLength(REASON_MAX_LENGTH)]
    public string? Reason { get; set; }
}
