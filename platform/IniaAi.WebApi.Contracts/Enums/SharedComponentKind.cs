namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Whether a shared component is a single block or a whole section.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind</c> — the two are
/// cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentKind
{
    /// <summary>A single reusable block.</summary>
    Block = 1,

    /// <summary>A whole reusable section including its inner blocks.</summary>
    Section = 2,
}
