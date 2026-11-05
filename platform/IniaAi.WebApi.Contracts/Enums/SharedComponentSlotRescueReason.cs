namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Why a slot pair with differing tags and content was still matched across two section
/// definitions.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Application.Common.Workflows.SharedComponentSlotRescueReason</c> — the two
/// are cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentSlotRescueReason
{
    /// <summary>The slots' blocks carry the same trimmed, case-insensitive display name.</summary>
    NameMatch = 1,

    /// <summary>The slots sit at the same entry position with sufficiently similar tags.</summary>
    PositionAndTagSimilarity = 2,
}
