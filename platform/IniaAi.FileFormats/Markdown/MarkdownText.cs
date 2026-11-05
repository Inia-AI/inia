using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Markdown;

internal static partial class MarkdownText
{
    public const string LINE_BREAK = "\n";
    public const string HARD_BREAK = "  \n";
    public const string BLOCK_SEPARATOR = "\n\n";
    public const string HORIZONTAL_RULE = "---";
    public const string BULLET = "- ";
    public const string ORDERED_SUFFIX = ". ";
    public const string QUOTE_PREFIX = "> ";
    public const string EMPTY_QUOTE_LINE = ">";
    public const string STRONG = "**";
    public const string EMPHASIS = "*";
    public const string STRIKETHROUGH = "~~";
    public const string CHECKED_TASK = "[x] ";
    public const string OPEN_TASK = "[ ] ";
    public const string LINK_TEXT_OPEN = "[";
    public const string LINK_TEXT_CLOSE = "](";
    public const string LINK_CLOSE = ")";
    public const string AUTOLINK_OPEN = "<";
    public const string AUTOLINK_CLOSE = ">";
    public const char BACKTICK = '`';
    public const char NEWLINE = '\n';
    public const char SPACE = ' ';
    public const int MIN_CODE_FENCE_LENGTH = 3;

    private const int MAX_HEADING_LEVEL = 6;
    private const char HEADING_MARK = '#';
    private const char ESCAPE = '\\';
    private const char UNDERSCORE = '_';
    private const char AMPERSAND = '&';
    private const string ALWAYS_ESCAPED = "\\`*[]<~";
    private const string TABLE_CELL_BREAK = "<br>";
    private const string ESCAPED_PIPE = "\\|";
    private const string PIPE = "|";
    private const string CELL_SEPARATOR = " | ";
    private const string ROW_START = "| ";
    private const string ROW_END = " |";
    private const string DELIMITER_CELL = "---";
    private const string SINGLE_SPACE = " ";

    private static readonly SearchValues<char> _mayNeedEscape = SearchValues.Create(ALWAYS_ESCAPED + UNDERSCORE + AMPERSAND);

    [GeneratedRegex(@"[ \t\r\n\f]{2,}|[\t\r\n\f]")]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex(@"^(#{1,6}(?=\s|$)|>|[-+](?=\s|$)|[-=]+\s*$)")]
    private static partial Regex BlockMarkerAtLineStartRegex();

    [GeneratedRegex(@"^(\d{1,9})([.)])(?=\s|$)")]
    private static partial Regex OrderedMarkerAtLineStartRegex();

    [GeneratedRegex(@"^&(#\d+|#[xX][0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);")]
    private static partial Regex EntityRegex();

    public static string CollapseWhitespace(string text)
    {
        return WhitespaceRunRegex().Replace(text, SINGLE_SPACE);
    }

    /// <summary>A heading whose text ends in a run of <c>#</c> escapes the run, which Markdown would otherwise read as the heading's closing marks.</summary>
    public static string Heading(int level, string text)
    {
        string line = CollapseWhitespace(text).Trim();
        int closing = line.Length - line.AsSpan().TrimEnd(HEADING_MARK).Length;
        if (closing > 0 && (closing == line.Length || line[^(closing + 1)] != ESCAPE))
        {
            line = line.Insert(line.Length - closing, ESCAPE.ToString());
        }

        return $"{new string(HEADING_MARK, Math.Min(MAX_HEADING_LEVEL, level))} {line}";
    }

    public static string Escape(string text)
    {
        if (text.AsSpan().IndexOfAny(_mayNeedEscape) < 0)
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (ALWAYS_ESCAPED.Contains(character)
                || (character == UNDERSCORE && !IsInsideWord(text, i))
                || (character == AMPERSAND && EntityRegex().IsMatch(text.AsSpan(i))))
            {
                _ = escaped.Append(ESCAPE);
            }

            _ = escaped.Append(character);
        }

        return escaped.ToString();
    }

    /// <summary>Escapes a line's first character when the line would otherwise open a heading, quote, list or rule.</summary>
    public static string EscapeLineStart(string line)
    {
        Match ordered = OrderedMarkerAtLineStartRegex().Match(line);
        if (ordered.Success)
        {
            return $"{ordered.Groups[1].Value}{ESCAPE}{line[ordered.Groups[1].Length..]}";
        }

        return BlockMarkerAtLineStartRegex().IsMatch(line) ? $"{ESCAPE}{line}" : line;
    }

    /// <summary>A pipe table whose first row is the header; every row is padded to the widest.</summary>
    public static string Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int columns = rows.Max(row => row.Count);
        var lines = new List<string> { Row(rows[0], columns), Row([.. Enumerable.Repeat(DELIMITER_CELL, columns)], columns) };
        lines.AddRange(rows.Skip(1).Select(row => Row(row, columns)));
        return string.Join(NEWLINE, lines);
    }

    private static string Row(IReadOnlyList<string> cells, int columns)
    {
        IEnumerable<string> padded = cells.Concat(Enumerable.Repeat(string.Empty, columns - cells.Count)).Select(Cell);
        return $"{ROW_START}{string.Join(CELL_SEPARATOR, padded)}{ROW_END}";
    }

    private static string Cell(string markdown)
    {
        return markdown
            .Replace(HARD_BREAK, TABLE_CELL_BREAK, StringComparison.Ordinal)
            .Replace(LINE_BREAK, TABLE_CELL_BREAK, StringComparison.Ordinal)
            .Replace(PIPE, ESCAPED_PIPE, StringComparison.Ordinal);
    }

    private static bool IsInsideWord(string text, int index)
    {
        return index > 0
            && index < text.Length - 1
            && char.IsLetterOrDigit(text[index - 1])
            && char.IsLetterOrDigit(text[index + 1]);
    }
}
