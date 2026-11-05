namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Whether a template's copy of a shared component still follows the Library or has been forked.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState</c> — the
/// two are cast between each other by integer value at the application boundary. Drift is guarded
/// by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentLinkState
{
    /// <summary>The copy follows the component head; fan-outs rewrite it in place.</summary>
    Linked = 1,

    /// <summary>The copy was deliberately forked; it keeps provenance but no longer follows the head.</summary>
    BrokenOut = 2,
}
