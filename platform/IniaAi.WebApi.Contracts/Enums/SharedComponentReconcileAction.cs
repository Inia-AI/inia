namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// The sanctioned per-instance decisions of a reconcile apply.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentReconcileAction</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentReconcileAction
{
    /// <summary>Push the instance's content as the component's new head and rewrite every Linked consumer.</summary>
    UpdateShared = 1,

    /// <summary>Rewrite this instance to the component head; relinks a broken-out copy.</summary>
    AdoptHead = 2,

    /// <summary>Fork this copy: keep content and provenance, stop following the head.</summary>
    BreakOut = 3,

    /// <summary>Explicitly leave the instance as it is.</summary>
    KeepLocal = 4,

    /// <summary>Re-attach a broken-out copy whose content still matches the head.</summary>
    Relink = 5,

    /// <summary>Suppress a definition-provided entry in this section copy only; entry-scoped.</summary>
    Hide = 6,

    /// <summary>Rematerialize a hidden entry at its definition position with current head content; entry-scoped.</summary>
    Unhide = 7,
}
