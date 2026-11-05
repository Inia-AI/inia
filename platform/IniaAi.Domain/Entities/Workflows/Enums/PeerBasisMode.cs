using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// How the Peer Financials block reconciles period kinds across the target and its peers.
/// </summary>
public enum PeerBasisMode
{
    /// <summary>
    /// One period kind for the whole table: the first configured preference every company
    /// can satisfy. A single peer lacking the preferred kind moves everyone to the next one.
    /// </summary>
    [EnumMember(Value = nameof(CommonBasis))]
    CommonBasis = 0,

    /// <summary>
    /// Each peer independently walks the preference list; period kinds may differ per column.
    /// </summary>
    [EnumMember(Value = nameof(Mixed))]
    Mixed = 1,

    /// <summary>
    /// The target's first available preference is the only acceptable kind; peers without it
    /// are left empty.
    /// </summary>
    [EnumMember(Value = nameof(Strict))]
    Strict = 2,
}
