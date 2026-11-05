namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How the target deployment's own copy of a shared component (or of the template) relates to the
/// one carried in an imported bundle, as computed by import-preview.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferRelation</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentTransferRelation
{
    /// <summary>No local counterpart exists — applying the import creates it.</summary>
    New = 1,

    /// <summary>The local and bundle heads already carry the same content.</summary>
    InSync = 2,

    /// <summary>The local chain already contains the bundle head as an ancestor and has moved past it.</summary>
    LocalAhead = 3,

    /// <summary>The bundle head descends from the local head — applying fast-forwards the local copy.</summary>
    BundleAhead = 4,

    /// <summary>Neither head descends from the other — the take-theirs / keep-mine arms resolve it.</summary>
    Diverged = 5,

    /// <summary>The correlated local component is retired — take-theirs is refused until it is unretired.</summary>
    LocalRetired = 6,
}
