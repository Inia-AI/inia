namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// The resolution the caller chose for a transfer element on apply: adopt the bundle's version or
/// keep this deployment's own.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm</c> — the
/// two are cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentTransferArm
{
    /// <summary>Adopt the bundle's version — append it as the new head and fan out to consumers.</summary>
    TakeTheirs = 1,

    /// <summary>Keep the local version — the bundle's is discarded for this element.</summary>
    KeepMine = 2,
}
