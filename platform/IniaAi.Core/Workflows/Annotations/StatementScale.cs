using System.Text.RegularExpressions;
using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// The scale a statement page prints its amounts in — a "£'000" or "$m" column header — so a
/// table cell reads as the amount it stands for: 2,506 under £'000 is £2,506,000. A page that
/// prints two different scales says nothing certain about any one cell, so it has none.
/// </summary>
internal sealed partial record StatementScale(UnitKind Kind, string? Subtype, double Multiplier)
{
    private const string THOUSAND = "thousand";
    private const string MILLION = "million";
    private const string BILLION = "billion";

    private static readonly Quantity _one = UnitExtractor.TryExtractSingle("1")
        ?? throw new InvalidOperationException("The unit lexicon reads no quantity from \"1\"");

    public static StatementScale? Of(string? pageText)
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return null;
        }

        List<string> headers = [.. ScaleHeader.Matches(pageText)
            .Select(match => $"{CurrencyCode(match.Groups["currency"].Value)} {Magnitude(match.Groups["scale"].Value)}")
            .Distinct()];

        if (headers.Count != 1)
        {
            return null;
        }

        Quantity unit = StatedUnits.WithStatedUnit(_one, headers[0]);
        return unit is { Kind: UnitKind.Currency, Multiplier: > 1.0 }
            ? new StatementScale(unit.Kind, unit.Subtype, unit.Multiplier)
            : null;
    }

    /// <summary>
    /// The amount a table cell stands for at this scale, or null for a number that is not an
    /// amount cell. Only a number written in thousands groups counts: a note reference, a
    /// headcount or a year on the same page is a bare number of another kind.
    /// </summary>
    public Quantity? Scale(Quantity cell)
    {
        if (!StatedUnits.StatesNoUnit(cell)
            || cell.Multiplier != 1.0
            || !GroupedDigitsRegex().IsMatch(cell.Surface))
        {
            return null;
        }

        return cell with { Kind = Kind, Subtype = Subtype, Multiplier = Multiplier, BaseValue = cell.Value * Multiplier };
    }

    /// <summary>
    /// Whether a cell among <paramref name="cells"/>, read at this scale, is exactly
    /// <paramref name="claimed"/>. A rounded figure is not stated: £2.5m is not the cell 2,506.
    /// </summary>
    public bool StatesScaled(IEnumerable<Quantity> cells, Quantity claimed)
    {
        return claimed.Kind == Kind
            && cells.Any(cell => Scale(cell) is Quantity scaled
                && scaled.Subtype == claimed.Subtype
                && QuantityMatcher.WithinTolerance(scaled.BaseValue, claimed.BaseValue, QuantityMatcher.SAME_AMOUNT_RELATIVE_ERROR));
    }

    private static string CurrencyCode(string currency)
    {
        return currency switch
        {
            "£" => "GBP",
            "$" or "US$" => "USD",
            "€" => "EUR",
            _ => currency.ToUpperInvariant(),
        };
    }

    /// <summary>The magnitude a header's scale word names; <see cref="ScaleHeader"/> admits only these.</summary>
    private static string Magnitude(string scale)
    {
        return char.ToLowerInvariant(scale[0]) switch
        {
            '0' or 't' or 'k' => THOUSAND,
            'b' => BILLION,
            _ => MILLION,
        };
    }

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+")]
    private static partial Regex GroupedDigitsRegex();
}
