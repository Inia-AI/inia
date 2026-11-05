namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Lifecycle state of a shared component in the Library registry.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentState</c> — the two are
/// cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentState
{
    /// <summary>The component is live and can be materialized into templates.</summary>
    Active = 1,

    /// <summary>The component is frozen; history is preserved and it can be un-retired.</summary>
    Retired = 2,
}
