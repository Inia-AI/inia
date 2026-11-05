namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// A quantity read with the unit stated beside it rather than in it: a statement's "£'000"
/// header, or the unit a validation extraction records next to a value. Statement scales and the
/// validation number reading both apply a stated unit this way.
/// </summary>
internal static class StatedUnits
{
    private static readonly char[] _hintSeparators = [' ', '\u00A0', ',', ';', '/'];

    /// <summary>
    /// The quantity a value denotes once its unit-as-stated is applied. The extractor is told to
    /// record the unit, scale or currency beside the value, so a scale can arrive in the hint
    /// while the value itself carries only a currency: the hint's unit supplies the kind and
    /// subtype and its magnitude the multiplier, each only where the value left that unstated,
    /// and the value's own always wins. A hint the lexicon does not know changes nothing.
    /// </summary>
    internal static Quantity WithStatedUnit(Quantity written, string? unitAsStated)
    {
        if (string.IsNullOrWhiteSpace(unitAsStated))
        {
            return written;
        }

        UnitKind kind = written.Kind;
        string? subtype = written.Subtype;
        double multiplier = written.Multiplier;
        bool changed = false;

        foreach (Quantity probe in HintProbes(unitAsStated))
        {
            if (StatesNoUnit(kind, subtype) && probe.Kind is not UnitKind.Plain)
            {
                kind = probe.Kind;
                subtype = probe.Subtype;
                changed = true;
            }

            if (multiplier == 1.0 && probe.Multiplier > 1.0)
            {
                multiplier = probe.Multiplier;
                changed = true;
            }

            if (!StatesNoUnit(kind, subtype) && multiplier != 1.0)
            {
                break;
            }
        }

        if (!changed)
        {
            return written;
        }

        return written with
        {
            Kind = kind,
            Subtype = subtype,
            Multiplier = multiplier,
            BaseValue = written.Value * multiplier,
        };
    }

    /// <summary>
    /// Whether a quantity carries no unit of its own. The lexicon types a bare 1900–2100 integer
    /// as a year, so a unit-free number is not always <see cref="UnitKind.Plain"/>: a NAV of
    /// 2,000 stated in USD thousands still has to take the hint, and a bare 2,000 in a table
    /// still has to ground it.
    /// </summary>
    internal static bool StatesNoUnit(Quantity quantity)
    {
        return StatesNoUnit(quantity.Kind, quantity.Subtype);
    }

    private static bool StatesNoUnit(UnitKind kind, string? subtype)
    {
        return subtype is null && kind is UnitKind.Plain or UnitKind.Year;
    }

    /// <summary>
    /// The hint read whole first, so a multi-word unit like "square feet" keeps its meaning,
    /// then word by word, because the lexicon pairs a magnitude with a number only when the two
    /// are adjacent — "USD millions" and "millions USD" have to read alike.
    /// </summary>
    private static IEnumerable<Quantity> HintProbes(string unitAsStated)
    {
        if (ReadSingleQuantity($"1 {unitAsStated}") is { } whole)
        {
            yield return whole;
        }

        foreach (string word in HintWords(unitAsStated))
        {
            if (ReadSingleQuantity($"1 {word}") is { } probe)
            {
                yield return probe;
            }
        }
    }

    internal static string[] HintWords(string unitAsStated)
    {
        return unitAsStated.Split(_hintSeparators, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static Quantity? ReadSingleQuantity(string text)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(text);

        return quantities.Count == 1 ? quantities[0] : null;
    }
}
