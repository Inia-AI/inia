namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Where an entry of a section copy comes from.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentEntryOrigin</c> —
/// the two are cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>SharedComponentEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum SharedComponentEntryOrigin
{
    /// <summary>A block the section definition provides as inline content.</summary>
    DefinitionInline = 1,

    /// <summary>A slot the section definition fills by referencing an independent Block component.</summary>
    DefinitionRef = 2,

    /// <summary>A block this workflow inserted locally; not part of the definition.</summary>
    LocalInsert = 3,
}
