using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Pairs numeric spans with unit / magnitude hits from the AC trie via a small
/// window around each number. Output is a list of <see cref="Quantity"/> records
/// — the structured form a <c>Fact.Quantity</c> carries.
///
/// Algorithm:
///   1. Scan numeric spans with a single regex that captures integers, decimals,
///      and grouped-with-commas numbers.
///   2. Run the AC trie over the full text in one pass to collect all candidate
///      unit / magnitude hits with their positions.
///   3. For each numeric span, look in a small left/right window for hits that:
///        - match position rules (currency symbol = prefix; SI unit = suffix;
///          ISO code = either side),
///        - pass a word-boundary check (short surfaces only fire when surrounded
///          by non-letter characters),
///        - sit in the immediate vicinity (whitespace/punctuation between the
///          number and the hit).
///   4. Resolve magnitude before unit so a magnitude word between number and
///      suffix unit doesn't block pairing ("754 million dollars"). Pick the
///      unit closest to the number, longest surface as tie-breaker.
///   5. Read the sign from the characters dressing the paired surface, so an
///      accounting negative — "(2,431)" — carries it, and take those characters
///      into the surface.
///   6. Emit a <see cref="Quantity"/> record.
///
/// A number joined to another by a date or range separator is skipped entirely:
/// the parts of "2024-01-15" are not quantities anyone can cite.
/// </summary>
public static partial class UnitExtractor
{
    private const int LEFT_WINDOW_CHARS = 8;
    private const int RIGHT_WINDOW_CHARS = 30;
    private const int NEGATION_WORD_WINDOW_CHARS = 24;
    private const int MIN_REMEMBERED_TEXT_LENGTH = 64;

    private static readonly Regex _numberRegex = GenerateNumberRegex();

    /// <summary>
    /// The quantities of each long text, kept while the text lives: one page, block answer or
    /// excerpt is read by many figures and facts. Keyed by the string instance, so a lookup costs
    /// no hash of the text. A short text is read again each time, as it costs less to read than to
    /// remember.
    /// </summary>
    private static readonly ConditionalWeakTable<string, IReadOnlyList<Quantity>> _quantitiesByText = new();

    public static IReadOnlyList<Quantity> Extract(string text)
    {
        return string.IsNullOrWhiteSpace(text) || text.Length < MIN_REMEMBERED_TEXT_LENGTH
            ? Extract(text, UnitDictionary.Instance)
            : _quantitiesByText.GetValue(text, static longText => [.. Extract(longText, UnitDictionary.Instance)]);
    }

    /// <summary>
    /// Convenience for callers that hold a string they expect to be a single
    /// quantity (a Fact value, a citation surface). Returns the extracted
    /// <see cref="Quantity"/> only when its surface covers the whole trimmed
    /// input — otherwise null. Used to populate <c>Fact.Quantity</c> and to
    /// derive a scope Quantity for matchers that take string parameters.
    /// </summary>
    public static Quantity? TryExtractSingle(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        IReadOnlyList<Quantity> extracted = Extract(trimmed);
        return extracted.FirstOrDefault(q => q.Surface == trimmed);
    }

    internal static IReadOnlyList<Quantity> Extract(string text, UnitDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var quantities = new List<Quantity>();

        var hits = new List<(int Start, int End, UnitEntry Entry)>();
        dictionary.Trie.ParseText(text, h =>
        {
            hits.Add((h.Begin, h.End, h.Value));
        });

        List<(int Start, int End)> scaleHeaders = ScaleHeader.ZeroHeaderSpans(text);

        foreach (Match m in _numberRegex.Matches(text))
        {
            int numStart = m.Index;
            int numEnd = m.Index + m.Length;

            if (IsComponentOfASeparatedToken(text, numStart, numEnd)
                || NumericalDataExtractor.Overlaps(numStart, numEnd, scaleHeaders))
            {
                continue;
            }

            if (!TryParseNumber(m.Value, out double parsed))
            {
                continue;
            }

            (UnitEntry? magnitude, int rightSearchStart) = FindMagnitudeAndAdvance(
                text, hits, numEnd);
            UnitEntry? unit = FindUnitForSpan(
                text, hits, numStart, numEnd, rightSearchStart);

            (int surfaceStart, int surfaceEnd) = ComputeSurfaceBounds(
                numStart, numEnd, rightSearchStart, unit, magnitude, hits, text);

            bool signStatedInWords = false;
            if (ReadSignMarkers(text, numStart, numEnd, surfaceStart, surfaceEnd)
                is (int markerStart, int markerEnd))
            {
                parsed = -parsed;
                surfaceStart = Math.Min(surfaceStart, markerStart);
                surfaceEnd = Math.Max(surfaceEnd, markerEnd);
            }
            else
            {
                signStatedInWords = NegationWordRegex().IsMatch(
                    text[Math.Max(0, surfaceStart - NEGATION_WORD_WINDOW_CHARS)..surfaceStart]);
            }

            UnitKind kind = unit?.Kind ?? UnitKind.Plain;
            string? subtype = unit?.Subtype;
            double multiplier = magnitude?.MagnitudeFactor ?? 1.0;
            double baseValue = parsed * multiplier;

            // Year detection: a bare 4-digit integer in [1900, 2100] with no
            // unit and no magnitude is overwhelmingly a calendar year in our
            // text distribution. Tagging as Year (zero tolerance) prevents the
            // 0.05% wildcard drift that let "2023" collide with "2024" under
            // the pre-Year matcher (Path 3 in findings.md). Conservative by
            // construction: any prefix unit, suffix unit, or magnitude wins
            // first and Year never triggers. A stated negative has already
            // moved the value out of the range, so "(2023)" never reads as one.
            if (kind is UnitKind.Plain
                && magnitude is null
                && parsed >= 1900
                && parsed <= 2100
                && parsed == Math.Truncate(parsed))
            {
                kind = UnitKind.Year;
            }

            quantities.Add(new Quantity(
                Kind: kind,
                Subtype: subtype,
                Value: parsed,
                Multiplier: multiplier,
                BaseValue: baseValue,
                Surface: text.Substring(surfaceStart, surfaceEnd - surfaceStart),
                Start: surfaceStart,
                End: surfaceEnd)
            {
                SignStatedInWords = signStatedInWords,
            });
        }

        return quantities;
    }

    /// <summary>
    /// Searches for a magnitude scalar immediately right of the number. If
    /// found, returns the entry and an advanced "right search start" position
    /// pointing past the magnitude word, so the unit pass can match a suffix
    /// unit that follows the magnitude (e.g. <c>"754 million dollars"</c>).
    /// </summary>
    private static (UnitEntry? Magnitude, int RightSearchStart) FindMagnitudeAndAdvance(
        string text,
        List<(int Start, int End, UnitEntry Entry)> hits,
        int numEnd)
    {
        foreach ((int hStart, int hEnd, UnitEntry entry) in hits)
        {
            if (entry.Kind is not UnitKind.Magnitude)
            {
                continue;
            }

            if (hStart < numEnd || !IsImmediateRightOf(text, numEnd, hStart))
            {
                continue;
            }

            if (entry.RequiresWordBoundary && !HasOuterWordBoundary(text, hStart, hEnd, isPrefix: false))
            {
                continue;
            }

            return (entry, hEnd);
        }

        return (null, numEnd);
    }

    private static UnitEntry? FindUnitForSpan(
        string text,
        List<(int Start, int End, UnitEntry Entry)> hits,
        int numStart,
        int numEnd,
        int rightSearchStart)
    {
        UnitEntry? best = null;
        int bestDistance = int.MaxValue;
        int bestLength = 0;

        foreach ((int hStart, int hEnd, UnitEntry entry) in hits)
        {
            if (entry.Kind is UnitKind.Magnitude)
            {
                continue;
            }

            bool isPrefixCandidate =
                hEnd <= numStart && IsImmediateLeftOf(text, hEnd, numStart);
            bool isSuffixCandidate =
                hStart >= rightSearchStart && IsImmediateRightOf(text, rightSearchStart, hStart);

            bool positionOk = entry.Position switch
            {
                UnitPosition.Prefix => isPrefixCandidate,
                UnitPosition.Suffix => isSuffixCandidate,
                UnitPosition.Either => isPrefixCandidate || isSuffixCandidate,
                _ => false,
            };

            if (!positionOk)
            {
                continue;
            }

            if (entry.RequiresWordBoundary && !HasOuterWordBoundary(text, hStart, hEnd, isPrefixCandidate))
            {
                continue;
            }

            // Distance from the number, in chars between the number boundary and
            // the hit. Prefix hits measured against numStart, suffix hits against
            // rightSearchStart (which is past any magnitude word).
            int distance = isPrefixCandidate
                ? numStart - hEnd
                : hStart - rightSearchStart;

            // Closer wins. Tie-break by longer surface so "kilogram" beats "kg"
            // at the same distance. Without distance ranking, post-magnitude
            // matches like " in" (length-inch) wrongly displaced "$" prefixes
            // for sentences like "$300 billion in revenue".
            int length = hEnd - hStart;
            bool better = distance < bestDistance
                || (distance == bestDistance && length > bestLength);
            if (better)
            {
                best = entry;
                bestDistance = distance;
                bestLength = length;
            }
        }

        return best;
    }

    private static (int Start, int End) ComputeSurfaceBounds(
        int numStart,
        int numEnd,
        int rightSearchStart,
        UnitEntry? unit,
        UnitEntry? magnitude,
        List<(int Start, int End, UnitEntry Entry)> hits,
        string text)
    {
        int start = numStart;
        int end = numEnd;

        if (unit is { Position: UnitPosition.Prefix or UnitPosition.Either })
        {
            (int hStart, int hEnd, _) = hits.LastOrDefault(
                h => ReferenceEquals(h.Entry, unit) && h.End <= numStart);
            if (hEnd > 0 && hEnd <= numStart && IsImmediateLeftOf(text, hEnd, numStart))
            {
                start = Math.Min(start, hStart);
            }
        }

        if (magnitude is not null)
        {
            (int hStart, int hEnd, _) = hits.FirstOrDefault(
                h => ReferenceEquals(h.Entry, magnitude) && h.Start >= numEnd);
            if (hEnd > 0 && hStart >= numEnd && IsImmediateRightOf(text, numEnd, hStart))
            {
                end = Math.Max(end, hEnd);
            }
        }

        if (unit is { Position: UnitPosition.Suffix or UnitPosition.Either })
        {
            (int hStart, int hEnd, _) = hits.FirstOrDefault(
                h => ReferenceEquals(h.Entry, unit) && h.Start >= rightSearchStart);
            if (hEnd > 0 && hStart >= rightSearchStart && IsImmediateRightOf(text, rightSearchStart, hStart))
            {
                end = Math.Max(end, hEnd);
            }
        }

        return (start, end);
    }

    private static bool IsImmediateLeftOf(string text, int hEnd, int numStart)
    {
        int gap = numStart - hEnd;
        if (gap is < 0 or > LEFT_WINDOW_CHARS)
        {
            return false;
        }

        for (int i = hEnd; i < numStart; i++)
        {
            if (!char.IsWhiteSpace(text[i]) && !IsNeutralPunct(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsImmediateRightOf(string text, int numEnd, int hStart)
    {
        int gap = hStart - numEnd;
        if (gap is < 0 or > RIGHT_WINDOW_CHARS)
        {
            return false;
        }

        for (int i = numEnd; i < hStart; i++)
        {
            if (!char.IsWhiteSpace(text[i]) && !IsNeutralPunct(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Boundary check that only inspects the side of the unit hit FACING AWAY
    /// from the number. The side facing the number is the number itself, which
    /// is the expected "boundary" — applying char.IsDigit there would falsely
    /// reject "$300" (the "3" is the number's start, not a word collision) and
    /// "88.7%" (the "7" is the number's tail). We require the OUTER side to be
    /// non-letter / non-digit so that surfaces don't collide with surrounding
    /// words ("USD" rejected inside "fooUSDbar", but accepted in "USD 50").
    /// </summary>
    private static bool HasOuterWordBoundary(string text, int hStart, int hEnd, bool isPrefix)
    {
        if (isPrefix)
        {
            if (hStart > 0)
            {
                char before = text[hStart - 1];
                if (char.IsLetter(before) || char.IsDigit(before))
                {
                    return false;
                }
            }
        }
        else
        {
            if (hEnd < text.Length)
            {
                char after = text[hEnd];
                if (char.IsLetter(after) || char.IsDigit(after))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsNeutralPunct(char c)
    {
        // Punctuation we permit between a number and its unit. Whitespace —
        // including ASCII space, NBSP (U+00A0), thin-space (U+2009),
        // narrow-NBSP (U+202F), tab — is handled separately by the
        // <see cref="char.IsWhiteSpace"/> check in the calling loop. Currency
        // symbols are deliberately NOT in this list: they are themselves
        // units and shouldn't be jumped over during number-unit pairing.
        return c is '-' or '−' or '–' or '—'
            or '(' or ')' or '[' or ']'
            or ',' or ';' or ':' or '.';
    }

    /// <summary>
    /// Whether the span is one component of a longer digit-and-separator token — the "01" and
    /// the "15" of "2024-01-15", or either half of "5-10". A date's parts are not quantities on
    /// their own: a fact claiming 15 is not grounded by a page that only prints 2024-01-15, and
    /// <see cref="NumericalGrounding"/> already refuses that on its literal tier. Every dash
    /// spelling counts, so a range typeset with an en dash reads like one typed with a hyphen;
    /// a dash only separates when digits sit on both sides, so the label dash of "Revenue — 1,234"
    /// leaves that figure alone. Comma and period groupings never reach here, because the number
    /// pattern consumes those whole.
    /// </summary>
    private static bool IsComponentOfASeparatedToken(string text, int numStart, int numEnd)
    {
        bool joinedLeft = numStart >= 2
            && IsComponentSeparator(text[numStart - 1])
            && char.IsAsciiDigit(text[numStart - 2]);

        bool joinedRight = numEnd + 1 < text.Length
            && IsComponentSeparator(text[numEnd])
            && char.IsAsciiDigit(text[numEnd + 1]);

        return joinedLeft || joinedRight;
    }

    private static bool IsComponentSeparator(char c)
    {
        return c is '-' or '−' or '–' or '—' or '/';
    }

    /// <summary>
    /// Whether the document dressed this number as a negative, and which characters said so.
    /// Only the dressing of the number itself decides: whitespace anywhere, and anything inside
    /// the paired surface except a bracket or a minus. Reading further turns a parenthetical
    /// aside into a negative — "Return (net) 12.5 (annualized)" — and a label separator into a
    /// minus sign — "Management fee - 150,000". The surface test cannot be a plain character
    /// window, because unit pairing steps over brackets, so "$(1,234)" and "(1,234) USD" carry
    /// their parenthesis inside the paired span.
    /// </summary>
    private static (int Start, int End)? ReadSignMarkers(
        string text,
        int numStart,
        int numEnd,
        int surfaceStart,
        int surfaceEnd)
    {
        int left = FirstOutsideDressing(text, numStart - 1, surfaceStart, surfaceEnd, forward: false);

        if (left < 0)
        {
            return null;
        }

        if (text[left] == '(')
        {
            int right = FirstOutsideDressing(text, numEnd, surfaceStart, surfaceEnd, forward: true);

            return right >= 0 && text[right] == ')' ? (left, right + 1) : null;
        }

        return IsMinusSign(text[left]) && CountsAsUnarySign(text, left, numStart, surfaceStart, surfaceEnd)
            ? (left, numEnd)
            : null;
    }

    /// <summary>
    /// A minus that touches the number is its sign. One separated by whitespace is a sign only
    /// when nothing but dressing precedes it on the line, which keeps "Management fee - 150,000"
    /// positive while "- 1,234" in a statement cell stays negative. The line, not the whole
    /// text, bounds that look-back: a document is not one value, and scanning to its start would
    /// cost the length of the document at every such marker.
    /// </summary>
    private static bool CountsAsUnarySign(string text, int marker, int numStart, int surfaceStart, int surfaceEnd)
    {
        for (int i = marker + 1; i < numStart; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return NothingPrecedesIt(text, marker, surfaceStart, surfaceEnd);
            }
        }

        return marker == 0 || !char.IsLetterOrDigit(text[marker - 1]);
    }

    /// <summary>
    /// Whether nothing at all but dressing precedes the marker. A minus held off the number by
    /// whitespace only signs it when the whole text is that one value — which is what an
    /// extracted field value is. The look-back deliberately does not stop at the line start: a
    /// document's own text is scanned here too, and "- 1,234 shares" opening a line is a
    /// markdown bullet far more often than a negative, so a line-bounded rule would negate
    /// every bulleted figure on an OCR'd page. Scanning backward keeps it cheap — whitespace is
    /// the only dressing outside the surface, so it stops at the first character that settles
    /// the answer rather than paying the whole prefix.
    /// </summary>
    private static bool NothingPrecedesIt(string text, int marker, int surfaceStart, int surfaceEnd)
    {
        for (int i = marker - 1; i >= 0; i--)
        {
            if (!IsDressing(text, i, surfaceStart, surfaceEnd))
            {
                return false;
            }
        }

        return true;
    }

    private static int FirstOutsideDressing(string text, int from, int surfaceStart, int surfaceEnd, bool forward)
    {
        int step = forward ? 1 : -1;

        for (int i = from; i >= 0 && i < text.Length; i += step)
        {
            if (!IsDressing(text, i, surfaceStart, surfaceEnd))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsDressing(string text, int index, int surfaceStart, int surfaceEnd)
    {
        if (char.IsWhiteSpace(text[index]))
        {
            return true;
        }

        if (index < surfaceStart || index >= surfaceEnd)
        {
            return false;
        }

        return !IsBracket(text[index]) && !IsMinusSign(text[index]);
    }

    private static bool IsBracket(char c)
    {
        return c is '(' or ')' or '[' or ']';
    }

    private static bool IsMinusSign(char c)
    {
        return c is '-' or '−';
    }

    private static bool TryParseNumber(string s, out double value)
    {
        return double.TryParse(
            s.Replace(",", string.Empty),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    /// <summary>
    /// A negation word ending right where the figure starts. "Declined to" and "fell to" name
    /// the level reached, not a fall, so only "by" or nothing may follow the verb.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:negative|minus|(?:losses|loss|deficit|outflows|outflow|decline|decrease|reduction) of|(?:declined|decreased|fell|dropped|shrank|contracted|down)(?: by)?|(?:reduced|lower) by)\s+$",
        RegexOptions.IgnoreCase | RegexOptions.RightToLeft)]
    private static partial Regex NegationWordRegex();

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.Compiled)]
    private static partial Regex GenerateNumberRegex();
}
