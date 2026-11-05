namespace IniaAi.Core.Common.Helpers;

/// <summary>
/// The sentence a span sits in, and the sentences a text splits into, under one rule: a full
/// stop, question mark or exclamation mark ends a sentence only when whitespace follows it, so
/// the decimal point of "1.5%" never cuts a passage in half, and a line break always ends one, so
/// a table row never bleeds into its neighbours. A full stop after a single letter ("L.P.",
/// "e.g.", "John F. Smith") or after a common abbreviation ("Inc.", "Dr.", "approx.") does not
/// end a sentence either, unless the letter is a figure's unit ("$5m.", "£(0.62)m."). Words that
/// often close a sentence, such as "etc." and "plc.", still end one.
/// </summary>
public static class TextPassage
{
    public const string ELLIPSIS = "\u2026";

    private static readonly HashSet<string> _abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st",
        "inc", "ltd", "co", "corp", "bros",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        "approx", "vs", "no", "nos", "fig", "figs", "pp", "vol", "ref", "cf",
    };

    /// <summary>
    /// The sentence holding <c>[start, end)</c>, cut to a window of about
    /// <paramref name="maxLength"/> around the span, with an ellipsis on each side that was cut,
    /// when the sentence runs long.
    /// </summary>
    public static string SentenceAround(string text, int start, int end, int maxLength)
    {
        int floor = Math.Max(start - maxLength, 0);
        int ceiling = Math.Min(end + maxLength, text.Length);
        int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1, start - floor) + 1;
        int lineEnd = text.IndexOf('\n', end, ceiling - end);

        if (lineEnd < 0)
        {
            lineEnd = ceiling;
        }

        lineStart = Math.Max(lineStart, floor);
        int sentenceStart = SentenceStartBefore(text, lineStart, start);
        int sentenceEnd = SentenceEndAfter(text, end, lineEnd);
        string sentence = text[sentenceStart..sentenceEnd].Trim();

        if (sentence.Length <= maxLength)
        {
            return sentence;
        }

        int slack = Math.Max(maxLength - (end - start), 0) / 2;
        int from = OffSurrogate(text, Math.Max(sentenceStart, start - slack));
        int to = OffSurrogate(text, Math.Min(sentenceEnd, end + slack));
        string window = text[from..to].Trim();

        return (from > sentenceStart ? ELLIPSIS : string.Empty)
            + window
            + (to < sentenceEnd ? ELLIPSIS : string.Empty);
    }

    /// <summary>The non-blank sentences of <paramref name="text"/>, in order.</summary>
    public static IEnumerable<string> Sentences(string text)
    {
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is '\n' || IsSentenceEnd(text, index))
            {
                string sentence = text[start..(index + 1)].Trim();
                if (sentence.Length > 0)
                {
                    yield return sentence;
                }

                start = index + 1;
            }
        }

        string last = text[start..].Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }

    private static bool IsSentenceEnd(string text, int index)
    {
        return (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1]))
            && (text[index] is '!' or '?' || (text[index] is '.' && !EndsAnAbbreviation(text, index)));
    }

    private static bool EndsAnAbbreviation(string text, int fullStop)
    {
        int wordStart = fullStop;
        while (wordStart > 0 && char.IsLetter(text[wordStart - 1]))
        {
            wordStart--;
        }

        int length = fullStop - wordStart;

        return (length == 1 && StartsAnInitial(text, wordStart))
            || (length > 1 && _abbreviations.Contains(text[wordStart..fullStop]));
    }

    /// <summary>
    /// A single letter is an initial when it starts the text or follows a space, another
    /// initial's full stop, or an opening bracket or quote. After a digit or a closing bracket it
    /// is a figure's unit: "$5m.", "£(0.62)m.".
    /// </summary>
    private static bool StartsAnInitial(string text, int letter)
    {
        return letter == 0
            || char.IsWhiteSpace(text[letter - 1])
            || text[letter - 1] is '.' or '(' or '[' or '"' or '\'' or '\u201C' or '\u2018';
    }

    private static int SentenceStartBefore(string text, int lineStart, int start)
    {
        for (int i = start - 1; i > lineStart; i--)
        {
            if (IsSentenceEnd(text, i))
            {
                return i + 1;
            }
        }

        return lineStart;
    }

    private static int SentenceEndAfter(string text, int end, int lineEnd)
    {
        for (int i = end; i < lineEnd; i++)
        {
            if (IsSentenceEnd(text, i))
            {
                return i + 1;
            }
        }

        return lineEnd;
    }

    /// <summary>
    /// A cut between a surrogate pair would emit a lone surrogate, so an index landing on a low
    /// surrogate moves off the pair rather than through it.
    /// </summary>
    private static int OffSurrogate(string text, int index)
    {
        return index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) ? index - 1 : index;
    }
}
