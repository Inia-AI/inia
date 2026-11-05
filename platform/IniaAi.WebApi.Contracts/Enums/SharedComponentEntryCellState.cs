namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How one consuming copy stands on one entry of a section component's definition.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix.SharedComponentEntryCellState</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentEntryCellState
{
    /// <summary>The copy carries the definition's content for this entry and follows the head.</summary>
    Live = 1,

    /// <summary>The entry has broken out in this copy: it keeps its own content and no longer follows the head.</summary>
    Custom = 2,

    /// <summary>The entry is hidden in this copy.</summary>
    Hidden = 3,

    /// <summary>A block this copy inserted locally; the definition has no entry for the slot.</summary>
    Added = 4,

    /// <summary>The definition owns this entry but the copy has no block for it.</summary>
    Missing = 5,

    /// <summary>The column exists only because another copy inserted a block there; this copy has nothing to say about it.</summary>
    NotApplicable = 6,
}
