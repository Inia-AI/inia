using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// A sign-in method a self-serve user has signed in with. The first sign-in by a method the account
/// has not used yet ends the account's other sessions, so a session started before the owner proved
/// the address cannot outlive that proof.
/// </summary>
[PrimaryKey(nameof(UserId), nameof(Method))]
public class UserSignInMethod
{
    public const int MAX_METHOD_LENGTH = 50;

    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public User User { get; set; } = null!;

    [StringLength(MAX_METHOD_LENGTH)]
    public string Method { get; set; } = string.Empty;

    public DateTime FirstSignedInAt { get; set; } = DateTime.UtcNow;
}
