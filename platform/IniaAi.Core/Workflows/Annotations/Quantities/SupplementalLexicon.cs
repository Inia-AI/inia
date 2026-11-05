namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Supplemental lexicon entries that fill known gaps in the CLDR-derived
/// dictionary. Three classes of entries:
///
/// 1. <b>English-default currency words</b>. CLDR's <c>USD.names</c> is
///    <c>["US dollar", "US dollars"]</c> — bare <c>"dollar"</c> /
///    <c>"dollars"</c> isn't there. Financial English uses the bare form to
///    mean USD; we add it explicitly. Same gymnastics for "cents".
///
/// 2. <b>Finance-specific units missing from CLDR</b>. <c>"basis points"</c>,
///    <c>"bp"</c>, <c>"bps"</c>, <c>"percentage points"</c>, <c>"pp"</c> —
///    standard finance vocabulary, not in CLDR. We register them as
///    <see cref="UnitKind.Concentration"/> with their own subtypes so the
///    matcher can distinguish 100bp from 100% (which differ by 100×).
///
/// 3. <b>Magnitude prefixes for tonnes</b>. CLDR has <c>mass-tonne</c> but no
///    <c>mass-megatonne</c> or <c>mass-kilotonne</c>; common in
///    sustainability / climate reporting.
///
/// Plus a couple of magnitude-scalar shorthand forms not in CLDR's currencies
/// section (e.g. <c>"MM"</c> for million, common in US finance).
/// </summary>
internal static class SupplementalLexicon
{
    public static IReadOnlyList<UnitEntry> GetEntries() =>
    [
        new UnitEntry("dollar", UnitKind.Currency, "USD", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("dollars", UnitKind.Currency, "USD", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("cent", UnitKind.Currency, "USD-cent", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("cents", UnitKind.Currency, "USD-cent", UnitPosition.Suffix, RequiresWordBoundary: true),

        new UnitEntry("basis points", UnitKind.Concentration, "concentr-basis-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("basis point", UnitKind.Concentration, "concentr-basis-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("bps", UnitKind.Concentration, "concentr-basis-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("bp", UnitKind.Concentration, "concentr-basis-point", UnitPosition.Suffix, RequiresWordBoundary: true),

        new UnitEntry("percentage points", UnitKind.Concentration, "concentr-percentage-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("percentage point", UnitKind.Concentration, "concentr-percentage-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("pp", UnitKind.Concentration, "concentr-percentage-point", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("ppt", UnitKind.Concentration, "concentr-percentage-point", UnitPosition.Suffix, RequiresWordBoundary: true),

        new UnitEntry("megatonnes", UnitKind.Mass, "mass-megatonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("megatonne", UnitKind.Mass, "mass-megatonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("Mt", UnitKind.Mass, "mass-megatonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("kilotonnes", UnitKind.Mass, "mass-kilotonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("kilotonne", UnitKind.Mass, "mass-kilotonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("kt", UnitKind.Mass, "mass-kilotonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("gigatonnes", UnitKind.Mass, "mass-gigatonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("gigatonne", UnitKind.Mass, "mass-gigatonne", UnitPosition.Suffix, RequiresWordBoundary: true),
        new UnitEntry("Gt", UnitKind.Mass, "mass-gigatonne", UnitPosition.Suffix, RequiresWordBoundary: true),

        new UnitEntry("MM", UnitKind.Magnitude, null, UnitPosition.Either, RequiresWordBoundary: true, MagnitudeFactor: 1e6),
        new UnitEntry("BB", UnitKind.Magnitude, null, UnitPosition.Either, RequiresWordBoundary: true, MagnitudeFactor: 1e9),
    ];
}
