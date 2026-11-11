using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// One consent a user gave at checkout, for a plan or a top-up, kept as evidence for the
/// confirmation email and for a dispute.
/// </summary>
[Index(nameof(OrganizationId))]
[Index(nameof(UserId))]
[Index(nameof(CheckoutSessionId))]
[Index(nameof(ConfirmationSentAt), nameof(ConfirmationAttempts), nameof(GivenAt))]
public class Consent
{
    public const int CLIENT_ADDRESS_MAX_LENGTH = 45;
    public const int CONFIRMATION_ERROR_MAX_LENGTH = 500;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    public ConsentKind Kind { get; set; }

    [Required]
    [StringLength(50)]
    public string TermsVersion { get; set; } = string.Empty;

    public DateTime GivenAt { get; set; } = DateTime.UtcNow;

    [StringLength(CLIENT_ADDRESS_MAX_LENGTH)]
    public string? ClientAddress { get; set; }

    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string? CheckoutSessionId { get; set; }

    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string? ProviderSubscriptionId { get; set; }

    /// <summary>The price of the top-up the checkout sold; empty for a plan's checkout.</summary>
    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string? TopUpPriceId { get; set; }

    /// <summary>The invoice that paid the top-up, once its checkout completes.</summary>
    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string? ProviderInvoiceId { get; set; }

    public DateTime? ConfirmationSentAt { get; set; }

    /// <summary>How many times the confirmation email of this checkout was tried; the same on each of its consents.</summary>
    public int ConfirmationAttempts { get; set; }

    public DateTime? ConfirmationLastAttemptAt { get; set; }

    /// <summary>Why the last try failed, while the email has not gone.</summary>
    [StringLength(CONFIRMATION_ERROR_MAX_LENGTH)]
    public string? ConfirmationLastError { get; set; }
}
