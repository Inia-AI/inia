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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
