namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// One numeric span found in text together with the unit (and optional magnitude
/// scalar) attached to it through window pairing. <c>BaseValue</c> is the
/// as-written magnitude (<c>Value</c> × <c>Multiplier</c>) in the subtype's own
/// unit — deliberately not converted to the dimension's base unit, because the
/// Plain-wildcard tier compares it against bare numbers exactly as the text
/// wrote them. Cross-subtype equivalence converts at comparison time through
/// <see cref="UnitConversionTable"/>. <c>Surface</c> is the literal extracted
/// text used for Tier-1 substring matching.
/// </summary>
public sealed record Quantity(
    UnitKind Kind,
    string? Subtype,
    double Value,
    double Multiplier,
    double BaseValue,
    string Surface,
    int Start,
    int End)
{
    /// <summary>
    /// A word right before the figure says it is negative — "negative 5.27%", "a loss of
    /// $0.8mn", "declined 42.38%" — while the digits carry no sign. Such a figure matches a
    /// source of either sign: the word states the direction the source's sign states.
    /// </summary>
    public bool SignStatedInWords { get; init; }

    /// <summary>A number with no unit and no magnitude word: it says nothing on its own.</summary>
    public bool IsBareNumber => Kind is UnitKind.Plain && Multiplier == 1.0;

    /// <summary>The same quantity with its sign dropped, for comparing magnitudes.</summary>
    public Quantity Magnitude()
    {
        return this with { Value = Math.Abs(Value), BaseValue = Math.Abs(BaseValue), SignStatedInWords = false };
    }
}
