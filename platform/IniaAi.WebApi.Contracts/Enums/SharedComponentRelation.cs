namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How a template's copy relates to its shared component, from the three-hash compare of instance
/// content, the link stamp, and the chain head.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentRelation</c> — the
/// two are cast between each other by integer value at the application boundary. Drift is guarded
/// by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentRelation
{
    /// <summary>The copy's content equals the component head.</summary>
    InSync = 1,

    /// <summary>The copy was edited locally while the head stayed where the copy last synced.</summary>
    LocalAhead = 2,

    /// <summary>The head moved past the copy, which still matches the version it was stamped from.</summary>
    HeadAhead = 3,

    /// <summary>Both the copy and the head changed since they last agreed.</summary>
    Diverged = 4,

    /// <summary>The copy's component id resolves to no registry entry in this deployment.</summary>
    ComponentMissing = 5,

    /// <summary>The component is retired; its definition is frozen.</summary>
    ComponentRetired = 6,
}
