namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// What apply actually did to one shared component on this deployment.
/// </summary>
/// <remarks>
/// Mirror of
/// <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferComponentAction</c> — the
/// two are cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentTransferComponentAction
{
    /// <summary>The component did not exist locally and was created.</summary>
    Created = 1,

    /// <summary>The bundle head was appended as the component's new head and fanned out to consumers.</summary>
    Appended = 2,

    /// <summary>The bundle head already matched the local head, so nothing was appended.</summary>
    Deduped = 3,

    /// <summary>The caller kept the local version — the bundle's was discarded.</summary>
    Kept = 4,
}
