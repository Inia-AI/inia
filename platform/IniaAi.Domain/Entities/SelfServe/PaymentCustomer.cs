using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// An organization's customer at one payment provider, made before its first checkout. The payment
/// worker finds the organization of a payment by it.
/// </summary>
[Index(nameof(OrganizationId))]
public class PaymentCustomer
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
