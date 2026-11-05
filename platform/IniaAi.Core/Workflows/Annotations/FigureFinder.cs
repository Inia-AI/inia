using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Where a text states a figure as written: as its own token with its sign, else as an
/// equivalent typed quantity ("$55.1 million" for "$55.1m") or the same date in another form.
/// A bare number with the same digits never counts — "Note 12" does not state "12%" — and nor
/// does a multiplier-stripped or partial match. The text's quantities and dates are read once for
/// every figure looked up in it.
/// </summary>
internal sealed class FigureFinder(string text)
{
    private IReadOnlyList<Quantity>? _quantities;
    private List<(int Position, string Value)>? _dates;

    public string Text { get; } = text;

    /// <summary>
    /// Where the text states the figure, and whether it states only a rounding of it: an amount
    /// that is equivalent within the matcher's tolerance but not the same amount.
    /// </summary>
    public (int Index, int Length, bool IsRounded) Locate(Fact figure)
    {
        foreach ((int Index, int Length) literal in NumericalGrounding.MatchesWithItsSign(Text, figure.Value))
        {
            return (literal.Index, literal.Length, false);
        }

        if (figure.Quantity is Quantity claimed)
        {
            _quantities ??= UnitExtractor.Extract(Text);
            return QuantityMatcher.FindBestMatch(claimed, _quantities) is { Tier: MatchTier.TypedQuantity } match
                ? (match.Quantity.Start, match.Quantity.End - match.Quantity.Start,
                    !QuantityMatcher.Equivalent(claimed, match.Quantity, QuantityMatcher.SAME_AMOUNT_RELATIVE_ERROR))
                : (-1, 0, false);
        }

        _dates ??= NumericalDataExtractor.ExtractDatePositions(Text);
        foreach ((int position, string statedDate) in _dates)
        {
            if (DateEquivalence.ValueStatesDate(statedDate, figure.Value))
            {
                return (position, statedDate.Length, false);
            }
        }

        return (-1, 0, false);
    }

    public bool States(Fact figure)
    {
        return Locate(figure).Index >= 0;
    }
}
