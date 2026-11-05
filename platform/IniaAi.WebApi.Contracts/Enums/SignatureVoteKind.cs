using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Direction of a vote cast on a pending signature override. Integer values
/// mirror <c>IniaAi.Domain.Entities.Signatures.SignatureVoteKind</c>.
/// </summary>
public enum SignatureVoteKind
{
    /// <summary>
    /// Voter agrees the pending override should become the active value.
    /// </summary>
    [EnumMember(Value = nameof(Approve))]
    Approve = 0,

    /// <summary>
    /// Voter disagrees with the pending override. Rejections may carry a
    /// comment and an optional suggested replacement value.
    /// </summary>
    [EnumMember(Value = nameof(Reject))]
    Reject = 1,
}
