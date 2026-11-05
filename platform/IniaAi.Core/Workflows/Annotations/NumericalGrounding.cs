using IniaAi.Core.Workflows.Annotations.Quantities;
using System.Globalization;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Shared three-step grounding for numerical-fact source verification:
/// (1) literal substring on number boundaries, (2) multiplier-stripped substring on number
/// boundaries, (3) an equivalent form via <see cref="StatesEquivalentValue"/> — kind-aware
/// quantity equivalence or the same calendar date written another way.
/// Centralised here so callers attaching a <see cref="Domain.Entities.Citations.Sources.DocumentSource"/>
/// to a numerical <see cref="Fact"/> can apply the same rule the
/// <see cref="FactExtractor"/> sweep uses.
/// </summary>
public static class NumericalGrounding
{
    /// <summary>
    /// Whether <paramref name="text"/> states <paramref name="value"/>
    /// literally, in multiplier-stripped form, as a kind-aware
    /// quantity-equivalent, or as the same calendar date in another form.
    /// Returns false for null/empty inputs.
    /// <para>
    /// The literal steps require number boundaries: "750" is not stated by text that only
    /// reads "1,204,750". This predicate decides whether a numerical fact keeps its source,
    /// so a fragment of a larger number must never read as grounding.
    /// </para>
    /// </summary>
    public static bool TextContainsValue(string? text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (StatesOnNumberBoundaries(text, value))
        {
            return true;
        }

        if (FactExtractor.StripMultiplierSuffix(value) is string coreNumber
            && StatesOnNumberBoundaries(text, coreNumber))
        {
            return true;
        }

        return StatesEquivalentValue(text, value);
    }

    /// <summary>
    /// Where <paramref name="text"/> states <paramref name="value"/> as its own number token and
    /// with the value's own sign: "-5.27%" does not state "5.27%". The sign is checked only for a
    /// value the extractor writes with its minus — a currency amount, a percentage, a multiple —
    /// because a plain number such as "1,250" is extracted without the minus it was written
    /// with, and so is a figure behind the true minus sign U+2212. Only a minus negates here; a
    /// parenthesis in prose is as often an aside as an accounting negative.
    /// </summary>
    public static IEnumerable<(int Index, int Length)> MatchesWithItsSign(string text, string value)
    {
        bool? keepsItsSign = null;

        return MatchesOnNumberBoundaries(text, value)
            .Where(match => !IsNegatedByAMinus(text, match.Index) || !(keepsItsSign ??= KeepsItsSign(value)));
    }

    public static bool StatesWithItsSign(string text, string value)
    {
        return MatchesWithItsSign(text, value).Any();
    }

    /// <summary>
    /// Whether a value is one the extractor writes with its minus, so that a minus before an
    /// unsigned occurrence of it marks another figure.
    /// </summary>
    public static bool KeepsItsSign(string value)
    {
        return UnitExtractor.TryExtractSingle(value) is { IsBareNumber: false, Kind: not UnitKind.Year }
            || value.EndsWith('x');
    }

    /// <summary>
    /// Whether a hyphen-minus right before <paramref name="index"/> negates the token there. A
    /// dash after a letter or digit joins a word, a range or a date instead: "FY-2025", "13-14%".
    /// </summary>
    public static bool IsNegatedByAMinus(string text, int index)
    {
        return index > 0
            && text[index - 1] == '-'
            && (index == 1 || !char.IsLetterOrDigit(text[index - 2]));
    }

    /// <summary>
    /// Every place <paramref name="text"/> states <paramref name="value"/> as its own number
    /// token rather than as a fragment of a longer one, ignoring letter case. An empty value is
    /// stated by nothing. The boundaries are the ones <see cref="WrapWithWordBoundaries"/> writes
    /// as a pattern, checked in place: a pattern per value would be parsed again on every call.
    /// </summary>
    public static IEnumerable<(int Index, int Length)> MatchesOnNumberBoundaries(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            yield break;
        }

        int from = 0;
        while (from <= text.Length - value.Length)
        {
            int index = text.IndexOf(value, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            int end = index + value.Length;
            if (StartsOnABoundary(text, index, value[0]) && EndsOnABoundary(text, end, value[^1]))
            {
                yield return (index, value.Length);
                from = end;
            }
            else
            {
                from = index + 1;
            }
        }
    }

    private static bool StartsOnABoundary(string text, int index, char first)
    {
        if (char.IsLetterOrDigit(first) && index > 0 && IsWordCharacter(text[index - 1]))
        {
            return false;
        }

        return !char.IsDigit(first)
            || index < 2
            || !char.IsDigit(text[index - 2])
            || text[index - 1] is not ('-' or '/' or '.' or ',');
    }

    private static bool EndsOnABoundary(string text, int end, char last)
    {
        if (end == text.Length)
        {
            return true;
        }

        char next = text[end];
        if (char.IsLetterOrDigit(last) && IsWordCharacter(next))
        {
            return false;
        }

        return !char.IsDigit(last)
            || (next is not ('-' or '/' or '%')
                && (next is not ('.' or ',') || end + 1 == text.Length || !char.IsDigit(text[end + 1])));
    }

    /// <summary>A character the regex word boundary treats as part of a word.</summary>
    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character)
            || character is '\u200C' or '\u200D'
            || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation;
    }

    /// <summary>
    /// Whether <paramref name="text"/> states <paramref name="value"/> in a form other than
    /// its surface form: as a kind-aware quantity equivalent, or as the same calendar date
    /// written another way.
    /// </summary>
    public static bool StatesEquivalentValue(string text, string value)
    {
        return QuantityMatcher.ContainsEquivalent(text, value)
            || DateEquivalence.TextStatesDate(text, value);
    }

    /// <summary>
    /// Whether <paramref name="text"/> contains <paramref name="value"/> as its own number
    /// token rather than as a fragment of a longer one. Letter case is ignored, as the
    /// literal tier always has been: an upper-case OCR header still states its date. An empty
    /// value is stated by nothing.
    /// </summary>
    internal static bool StatesOnNumberBoundaries(string text, string value)
    {
        return LocateOnNumberBoundaries(text, value).Index >= 0;
    }

    /// <summary>
    /// Position of <paramref name="value"/> in <paramref name="text"/>, with the matched length so
    /// callers can centre on it: the value as its own number token, then its multiplier-stripped
    /// form, then the span that states it in another form (<see cref="StatesEquivalentValue"/>),
    /// and only then either form inside a longer token. Returns <c>(-1, 0)</c> when no form is
    /// present.
    /// <para>
    /// The search inside longer tokens comes last because a short stripped number ("12") sits
    /// inside unrelated ones ("2012") often enough to centre the window away from the fact. It
    /// stays as the last resort because a miss leaves over-long cited text untruncated.
    /// </para>
    /// </summary>
    public static (int Index, int Length) LocateValue(string? text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return (-1, 0);
        }

        string? coreNumber = FactExtractor.StripMultiplierSuffix(value);

        (int Index, int Length) located = LocateOnNumberBoundaries(text, value);
        if (located.Index < 0 && coreNumber is not null)
        {
            located = LocateOnNumberBoundaries(text, coreNumber);
        }

        if (located.Index < 0)
        {
            located = LocateEquivalentValue(text, value);
        }

        if (located.Index < 0)
        {
            located = LocateAnywhere(text, value);
        }

        if (located.Index < 0 && coreNumber is not null)
        {
            located = LocateAnywhere(text, coreNumber);
        }

        return located;
    }

    private static (int Index, int Length) LocateOnNumberBoundaries(string text, string value)
    {
        foreach ((int Index, int Length) match in MatchesOnNumberBoundaries(text, value))
        {
            return match;
        }

        return (-1, 0);
    }

    private static (int Index, int Length) LocateAnywhere(string text, string value)
    {
        int index = value.Length == 0 ? -1 : text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? (index, value.Length) : (-1, 0);
    }

    private static (int Index, int Length) LocateEquivalentValue(string text, string value)
    {
        if (UnitExtractor.TryExtractSingle(value) is Quantity claimed
            && QuantityMatcher.FindBestMatch(claimed, UnitExtractor.Extract(text)) is QuantityMatch match)
        {
            return (match.Quantity.Start, match.Quantity.End - match.Quantity.Start);
        }

        if (!DateEquivalence.IsDate(value))
        {
            return (-1, 0);
        }

        foreach ((int position, string statedDate) in NumericalDataExtractor.ExtractDatePositions(text))
        {
            if (DateEquivalence.ValueStatesDate(statedDate, value))
            {
                return (position, statedDate.Length);
            }
        }

        return (-1, 0);
    }

    /// <summary>
    /// Wraps an escaped pattern so it only matches a whole number token. Word boundaries stop
    /// letter and digit adjacency; the lookbehind and lookahead stop date separators, decimal
    /// points, thousands-group commas and percent signs, so "15" does not match inside
    /// "2024-01-15", "3" inside "5.3", "751" inside "13,751", or "100" inside "100%".
    /// </summary>
    public static string WrapWithWordBoundaries(string escapedPattern, string originalValue)
    {
        if (string.IsNullOrEmpty(originalValue))
        {
            return escapedPattern;
        }

        string result = escapedPattern;
        bool startsWithDigit = char.IsDigit(originalValue[0]);
        bool endsWithDigit = char.IsDigit(originalValue[^1]);

        if (char.IsLetterOrDigit(originalValue[0]))
        {
            result = @"\b" + result;
        }

        if (startsWithDigit)
        {
            result = @"(?<!\d[-/.,])" + result;
        }

        if (char.IsLetterOrDigit(originalValue[^1]))
        {
            result += @"\b";
        }

        if (endsWithDigit)
        {
            result += @"(?![-/%]|\.\d|,\d)";
        }

        return result;
    }
}
