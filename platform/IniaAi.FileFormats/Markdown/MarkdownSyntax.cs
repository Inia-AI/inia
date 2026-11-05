using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Markdown;

/// <summary>Decides whether text carries Markdown that a Markdown reader would render.</summary>
public static partial class MarkdownSyntax
{
    /// <summary>
    /// Paired emphasis (**bold**, __bold__, *italic*), an ATX heading, a bullet or numbered list marker, or a block
    /// quote, each anchored so that a stray asterisk or a leading hyphen inside prose does not count.
    /// </summary>
    public static bool IsLikelyMarkdown(string text)
    {
        return MarkdownPattern().IsMatch(text);
    }

    /// <summary>
    /// <see cref="IsLikelyMarkdown"/>, or a GitHub pipe table, which carries none of that syntax but still needs the
    /// Markdown reader to become a table.
    /// </summary>
    public static bool IsLikelyMarkdownOrTable(string text)
    {
        return IsLikelyMarkdown(text) || PipeTableRowPattern().IsMatch(text);
    }

    [GeneratedRegex(
        @"(?:\*\*[^*\r\n]+\*\*)|(?:__[^_\r\n]+__)|(?:(?<![\w*])\*[^\s*][^*\r\n]*\*(?![\w*]))|(?:^[ \t]{0,3}#{1,6}[ \t])|(?:^[ \t]*[-*+][ \t]+\S)|(?:^[ \t]*\d+\.[ \t]+\S)|(?:^[ \t]*>[ \t]?)",
        RegexOptions.Multiline)]
    private static partial Regex MarkdownPattern();

    [GeneratedRegex(@"^[ \t]*\|.*\|[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex PipeTableRowPattern();
}
