using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// The code attempts of one address since its last sign-in, counted across codes, so a new code does
/// not reset the count. An attempt counts before its code is compared, so a burst of guesses gets no
/// more than the limit. It exists whether or not the address has an account, so a lockout reveals
/// nothing.
/// </summary>
public class EmailCodeLockout
{
    [Key]
    [StringLength(EmailCodeRequest.HASH_LENGTH)]
    public string AddressHash { get; set; } = string.Empty;

    public int FailedAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime LastFailedAt { get; set; } = DateTime.UtcNow;
}
