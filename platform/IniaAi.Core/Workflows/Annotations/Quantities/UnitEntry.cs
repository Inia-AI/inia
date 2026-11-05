namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// One row in the unit lexicon. The AC trie holds these as payloads;
/// each surface form (singular, plural, abbreviation, ISO code, symbol)
/// gets its own entry pointing at the same canonical metadata.
/// </summary>
internal sealed record UnitEntry(
    string Surface,
    UnitKind Kind,
    string? Subtype,
    UnitPosition Position,
    bool RequiresWordBoundary,
    double MagnitudeFactor = 1.0);
