using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// A payment provider's event, stored once when it arrives and applied later by the payment worker,
/// which leases each unprocessed row and retries it.
/// </summary>
[Index(nameof(PaymentProvider), nameof(ProviderEventId), IsUnique = true)]
[Index(nameof(ProcessedAt), nameof(ReceivedAt))]
[Index(nameof(OrganizationId))]
public class PaymentEvent
{
    public const int TYPE_MAX_LENGTH = 100;
    public const int OUTCOME_MAX_LENGTH = 100;
    public const int LAST_ERROR_MAX_LENGTH = 2000;
    public const int PROVIDER_EVENT_ID_MAX_LENGTH = CreditPack.PROVIDER_ID_MAX_LENGTH;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(CreditPack.PAYMENT_PROVIDER_MAX_LENGTH)]
    public string PaymentProvider { get; set; } = string.Empty;

    [Required]
    [StringLength(PROVIDER_EVENT_ID_MAX_LENGTH)]
    public string ProviderEventId { get; set; } = string.Empty;

    [Required]
    [StringLength(TYPE_MAX_LENGTH)]
    public string Type { get; set; } = string.Empty;

    /// <summary>The organization the event belongs to, once the worker has found it.</summary>
    [StringLength(450)]
    public string? OrganizationId { get; set; }

    /// <summary>The event as the provider sent it.</summary>
    public string? Payload { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    [StringLength(OUTCOME_MAX_LENGTH)]
    public string? Outcome { get; set; }

    public int Attempts { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }

    [StringLength(LAST_ERROR_MAX_LENGTH)]
    public string? LastError { get; set; }
}
