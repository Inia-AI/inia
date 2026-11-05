namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// Direction of a vote cast on a <see cref="PendingSignatureOverride"/>.
/// </summary>
public enum SignatureVoteKind
{
    /// <summary>
    /// Voter agrees the pending override should become the active value.
    /// </summary>
    Approve = 0,

    /// <summary>
    /// Voter disagrees with the pending override. Rejections may carry a
    /// <see cref="SignatureOverrideVote.Comment"/> and an optional
    /// <see cref="SignatureOverrideVote.SuggestedValue"/>.
    /// </summary>
    Reject = 1,
}
