namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How import-preview matched a bundle component to a local one — disclosed so the caller can weigh
/// a relation that rests on a weaker signal (a stable-id anchor or a bare id match) differently from
/// one backed by shared content history.
/// </summary>
/// <remarks>
/// Mirror of
/// <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentTransferCorrelationMethod
{
    /// <summary>The local template's node at the same stable id links a local component of the same kind.</summary>
    Anchor = 1,

    /// <summary>The bundle's hash trail overlaps the local chain's hashes.</summary>
    HashTrail = 2,

    /// <summary>A local component already holds the bundle component's id with the same kind.</summary>
    GuidEquality = 3,

    /// <summary>No local counterpart — the component is new to this deployment.</summary>
    Create = 4,
}
