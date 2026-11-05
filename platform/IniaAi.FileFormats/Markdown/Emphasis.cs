using System.Text;

namespace IniaAi.FileFormats.Markdown;

/// <summary>
/// Bold, italic and strikethrough are written as placeholder characters first, and chosen once the
/// whole paragraph is known: Markdown opens or closes a mark only beside the right characters, so a
/// mark whose neighbours would leave literal asterisks becomes an HTML tag, and two touching runs of
/// the same mark become one.
/// </summary>
internal static class Emphasis
{
    public enum Kind
    {
        Strong,
        Emphasis,
        Strikethrough,
    }

    private const char FIRST_MARKER = '';
    private const char LAST_MARKER = '';

    private static readonly (string Markdown, string HtmlOpen, string HtmlClose)[] _marks =
    [
        (MarkdownText.STRONG, "<strong>", "</strong>"),
        (MarkdownText.EMPHASIS, "<em>", "</em>"),
        (MarkdownText.STRIKETHROUGH, "<del>", "</del>"),
    ];

    /// <summary>Marks the text and leaves its outer whitespace outside the marks, where Markdown needs it.</summary>
    public static string Wrap(string inline, Kind kind)
    {
        string core = inline.Trim();
        if (core.Length == 0)
        {
            return inline;
        }

        string leading = inline[..(inline.Length - inline.TrimStart().Length)];
        string trailing = inline[inline.TrimEnd().Length..];
        return $"{leading}{Open(kind)}{core}{Close(kind)}{trailing}";
    }

    public static string WithoutMarkers(string text)
    {
        return text.AsSpan().IndexOfAnyInRange(FIRST_MARKER, LAST_MARKER) < 0
            ? text
            : string.Concat(text.Where(character => !IsMarker(character)));
    }

    public static string Resolve(string inline)
    {
        if (inline.AsSpan().IndexOfAnyInRange(FIRST_MARKER, LAST_MARKER) < 0)
        {
            return inline;
        }

        string text = inline;
        foreach (Kind kind in Enum.GetValues<Kind>())
        {
            text = text.Replace($"{Close(kind)}{Open(kind)}", string.Empty, StringComparison.Ordinal);
        }

        var replacements = new string?[text.Length];
        var opened = new Stack<int>();
        for (int index = 0; index < text.Length; index++)
        {
            if (!IsMarker(text[index]))
            {
                continue;
            }

            if (IsOpening(text[index]))
            {
                opened.Push(index);
                continue;
            }

            if (opened.Count > 0)
            {
                int start = opened.Pop();
                (string markdown, string htmlOpen, string htmlClose) = _marks[(text[index] - FIRST_MARKER) / 2];
                bool flanks = CanOpen(text, start) && CanClose(text, index);
                replacements[start] = flanks ? markdown : htmlOpen;
                replacements[index] = flanks ? markdown : htmlClose;
            }
        }

        var resolved = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            _ = IsMarker(text[index]) ? resolved.Append(replacements[index]) : resolved.Append(text[index]);
        }

        return resolved.ToString();
    }

    private static char Open(Kind kind)
    {
        return (char)(FIRST_MARKER + (2 * (int)kind));
    }

    private static char Close(Kind kind)
    {
        return (char)(Open(kind) + 1);
    }

    private static bool IsMarker(char character)
    {
        return character is >= FIRST_MARKER and <= LAST_MARKER;
    }

    private static bool IsOpening(char marker)
    {
        return (marker - FIRST_MARKER) % 2 == 0;
    }

    /// <summary>The CommonMark rule for a left-flanking run: not before whitespace, and before punctuation only after whitespace or punctuation.</summary>
    private static bool CanOpen(string text, int index)
    {
        char? before = Neighbour(text, index, -1);
        char? after = Neighbour(text, index, 1);
        if (after is not char next || char.IsWhiteSpace(next))
        {
            return false;
        }

        return !IsPunctuation(next) || before is not char previous || char.IsWhiteSpace(previous) || IsPunctuation(previous);
    }

    /// <summary>The CommonMark rule for a right-flanking run: not after whitespace, and after punctuation only before whitespace or punctuation.</summary>
    private static bool CanClose(string text, int index)
    {
        char? before = Neighbour(text, index, -1);
        char? after = Neighbour(text, index, 1);
        if (before is not char previous || char.IsWhiteSpace(previous))
        {
            return false;
        }

        return !IsPunctuation(previous) || after is not char next || char.IsWhiteSpace(next) || IsPunctuation(next);
    }

    private static char? Neighbour(string text, int index, int step)
    {
        for (int position = index + step; position >= 0 && position < text.Length; position += step)
        {
            if (!IsMarker(text[position]))
            {
                return text[position];
            }
        }

        return null;
    }

    private static bool IsPunctuation(char character)
    {
        return char.IsPunctuation(character) || char.IsSymbol(character);
    }
}
