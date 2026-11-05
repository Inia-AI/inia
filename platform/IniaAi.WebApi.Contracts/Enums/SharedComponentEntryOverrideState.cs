namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// A section copy's per-entry deviation from its definition.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentEntryOverrideState</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentEntryOverrideState
{
    /// <summary>The entry follows the definition.</summary>
    None = 1,

    /// <summary>The entry is suppressed in this copy only; the definition keeps providing it.</summary>
    Hidden = 2,

    /// <summary>The entry's block was forked locally; grafts leave its content untouched.</summary>
    BrokenOut = 3,
}
