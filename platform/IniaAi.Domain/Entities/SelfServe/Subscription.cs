using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// An organization's subscription with one payment provider, in the platform's own statuses.
/// The row can exist before the provider creates the subscription, to hold the customer the
/// organization's first checkout uses.
/// </summary>
[Index(nameof(OrganizationId))]
[Index(nameof(PaymentProvider), nameof(ProviderSubscriptionId), IsUnique = true)]
[Index(nameof(PaymentProvider), nameof(LiveOrganizationId), IsUnique = true)]
[Index(nameof(EndNoticeSentAt), nameof(CanceledAt))]
public class Subscription
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(CreditPack.PAYMENT_PROVIDER_MAX_LENGTH)]
    public string PaymentProvider { get; set; } = string.Empty;

    [Required]
    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string ProviderCustomerId { get; set; } = string.Empty;

    [StringLength(CreditPack.PROVIDER_ID_MAX_LENGTH)]
    public string? ProviderSubscriptionId { get; set; }

    public SubscriptionStatus Status { get; set; }

    /// <summary>
    /// The organization id while the subscription is live, and empty once it ends, so a unique index
    /// allows one live subscription per organization with each provider.
    /// </summary>
    [StringLength(450)]
    public string? LiveOrganizationId { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>
    /// When the customer last asked to end the subscription, as the provider records it: a new time
    /// for each cancel, and empty again after a resume. The end-of-contract email goes once for each.
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>When the subscription ends or ended; empty while it renews.</summary>
    public DateTime? EndsAt { get; set; }

    public DateTime? EndNoticeSentAt { get; set; }

    /// <summary>How many times the end-of-contract email for <see cref="CanceledAt"/> was tried.</summary>
    public int EndNoticeAttempts { get; set; }

    public DateTime? EndNoticeLastAttemptAt { get; set; }

    /// <summary>Why the last try failed, while the email has not gone.</summary>
    [StringLength(Consent.CONFIRMATION_ERROR_MAX_LENGTH)]
    public string? EndNoticeLastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
