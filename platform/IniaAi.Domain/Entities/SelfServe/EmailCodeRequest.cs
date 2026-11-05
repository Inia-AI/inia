using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// One sign-in code sent by email. The address and the code are stored only as keyed hashes, so the
/// table holds no readable address and no code a million guesses would find.
/// </summary>
[Index(nameof(AddressHash), nameof(CreatedAt))]
public class EmailCodeRequest
{
    /// <summary>A keyed SHA-256 hash in hexadecimal.</summary>
    public const int HASH_LENGTH = 64;

    /// <summary>The longest client address: an IPv6 address in text.</summary>
    public const int CLIENT_ADDRESS_LENGTH = 45;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(HASH_LENGTH)]
    public string AddressHash { get; set; } = string.Empty;

    [Required]
    [StringLength(HASH_LENGTH)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set once, by an update that requires it to be empty, so a code works one time.</summary>
    public DateTime? UsedAt { get; set; }

    [StringLength(CLIENT_ADDRESS_LENGTH)]
    public string? ClientAddress { get; set; }
}
