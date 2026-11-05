using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// A single vote cast by a reviewer on a <see cref="PendingSignatureOverride"/>.
/// The proposer's submission is seeded as an Approve vote so the tenant threshold
/// counts it automatically. Unique per <c>(PendingOverrideId, VoterUserId)</c>, so
/// the same user cannot vote twice on the same pending.
/// </summary>
[Index(nameof(PendingOverrideId), nameof(VoterUserId), IsUnique = true)]
public class SignatureOverrideVote : Entity<SignatureOverrideVote>
{
    [Required]
    [StringLength(450)]
    public string PendingOverrideId { get; set; } = string.Empty;

    [ForeignKey(nameof(PendingOverrideId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public PendingSignatureOverride? Pending { get; set; }

    [Required]
    [StringLength(450)]
    public string VoterUserId { get; set; } = string.Empty;

    public SignatureVoteKind VoteKind { get; set; }

    [StringLength(2000)]
    public string? Comment { get; set; }

    [StringLength(4000)]
    public string? SuggestedValue { get; set; }
}
