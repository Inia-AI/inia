namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How a section definition fills one of its entries.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.Workflows.Enums.SectionEntryKind</c> — the two are cast
/// between each other by integer value at the application boundary. Drift is guarded by
/// <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SectionEntryKind
{
    /// <summary>The definition provides the entry's content itself.</summary>
    Inline = 1,

    /// <summary>The definition fills the entry by referencing an independent Block component.</summary>
    ComponentRef = 2,
}
