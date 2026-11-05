using Microsoft.AspNetCore.Components;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Word-level diff of two texts for the version-history change dialog, or line-level for a list
/// whose lines are whole items, rendered as a single unified HTML stream: unchanged words plain,
/// removed words in an error-tinted, struck-through mark, added words in a success-tinted,
/// underlined mark (styled like the editor's replacement-tag pills). Visually hidden text before
/// and after each mark tells a screen reader which kind of change it is and where it ends.
/// All text is HTML-encoded; the marks and those hidden labels are the only markup.
/// </summary>
public static partial class TextDiffHighlighter
{
    private const string REMOVED_MARK_OPEN = "<span class=\"mud-sr-only\">removed: </span><mark class=\"diff-removed\">";
    private const string REMOVED_MARK_CLOSE = "</mark><span class=\"mud-sr-only\"> (end of removed) </span>";
    private const string ADDED_MARK_OPEN = "<span class=\"mud-sr-only\">added: </span><mark class=\"diff-added\">";
    private const string ADDED_MARK_CLOSE = "</mark><span class=\"mud-sr-only\"> (end of added) </span>";

    /// <summary>
    /// Beyond this LCS table size the middle sections are treated as one wholesale
    /// remove-then-add rather than word-matched, keeping worst-case cost bounded.
    /// </summary>
    private const int MAX_LCS_CELLS = 4_000_000;

    public static MarkupString HighlightMerged(string? before, string? after)
    {
        return Merge(Tokenize(before ?? string.Empty, TokenPattern()), Tokenize(after ?? string.Empty, TokenPattern()));
    }

    public static MarkupString HighlightMergedLines(string? before, string? after)
    {
        return Merge(Tokenize(before ?? string.Empty, LinePattern()), Tokenize(after ?? string.Empty, LinePattern()));
    }

    private static MarkupString Merge(string[] beforeTokens, string[] afterTokens)
    {
        // Tokens carry their trailing whitespace for faithful rendering, but equality is
        // decided on the visible core only — otherwise inserting a blank line after a word
        // would flag the word itself as removed-and-added.
        string[] beforeCores = [.. beforeTokens.Select(token => token.Trim())];
        string[] afterCores = [.. afterTokens.Select(token => token.Trim())];

        int prefix = CommonPrefixLength(beforeCores, afterCores);
        int suffix = CommonSuffixLength(beforeCores, afterCores, prefix);

        string[] beforeMiddle = beforeTokens[prefix..(beforeTokens.Length - suffix)];
        string[] afterMiddle = afterTokens[prefix..(afterTokens.Length - suffix)];
        string[] beforeMiddleCores = beforeCores[prefix..(beforeCores.Length - suffix)];
        string[] afterMiddleCores = afterCores[prefix..(afterCores.Length - suffix)];

        bool[] beforeKept;
        bool[] afterKept;
        if ((long)beforeMiddle.Length * afterMiddle.Length > MAX_LCS_CELLS)
        {
            beforeKept = new bool[beforeMiddle.Length];
            afterKept = new bool[afterMiddle.Length];
        }
        else
        {
            (beforeKept, afterKept) = LongestCommonSubsequenceKeeps(beforeMiddleCores, afterMiddleCores);
        }

        // Unchanged stretches render from the after side so the current spacing wins.
        var html = new StringBuilder();
        AppendEncoded(html, afterTokens.AsSpan(0, prefix));
        AppendMergedMiddle(html, beforeMiddle, beforeKept, afterMiddle, afterKept);
        AppendEncoded(html, afterTokens.AsSpan(afterTokens.Length - suffix));

        return new MarkupString(html.ToString());
    }

    private static void AppendMergedMiddle(
        StringBuilder html,
        string[] beforeMiddle,
        bool[] beforeKept,
        string[] afterMiddle,
        bool[] afterKept)
    {
        int b = 0;
        int a = 0;
        while (b < beforeMiddle.Length || a < afterMiddle.Length)
        {
            // Added run renders before the removed run: new text appears where the author
            // typed it, with what it displaced struck out after it.
            int addedStart = a;
            while (a < afterMiddle.Length && !afterKept[a])
            {
                a++;
            }

            if (a > addedStart)
            {
                _ = html.Append(ADDED_MARK_OPEN);
                AppendEncoded(html, afterMiddle.AsSpan(addedStart, a - addedStart));
                _ = html.Append(ADDED_MARK_CLOSE);
            }

            int removedStart = b;
            while (b < beforeMiddle.Length && !beforeKept[b])
            {
                b++;
            }

            if (b > removedStart)
            {
                _ = html.Append(REMOVED_MARK_OPEN);
                AppendEncoded(html, beforeMiddle.AsSpan(removedStart, b - removedStart));
                _ = html.Append(REMOVED_MARK_CLOSE);
            }

            // Both cursors now sit on the next common (kept) token; emit it once from the
            // after side so its current trailing whitespace is preserved.
            if (b < beforeMiddle.Length && a < afterMiddle.Length)
            {
                _ = html.Append(WebUtility.HtmlEncode(afterMiddle[a]));
                b++;
                a++;
            }
        }
    }

    private static void AppendEncoded(StringBuilder html, ReadOnlySpan<string> tokens)
    {
        foreach (string token in tokens)
        {
            _ = html.Append(WebUtility.HtmlEncode(token));
        }
    }

    private static string[] Tokenize(string text, Regex pattern)
    {
        return [.. pattern.Matches(text).Select(match => match.Value)];
    }

    [GeneratedRegex(@"\S+\s*|\s+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[^\n]*\n|[^\n]+")]
    private static partial Regex LinePattern();

    private static int CommonPrefixLength(string[] before, string[] after)
    {
        int max = Math.Min(before.Length, after.Length);
        int count = 0;
        while (count < max && string.Equals(before[count], after[count], StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static int CommonSuffixLength(string[] before, string[] after, int prefix)
    {
        int max = Math.Min(before.Length, after.Length) - prefix;
        int count = 0;
        while (count < max
            && string.Equals(before[^(count + 1)], after[^(count + 1)], StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static (bool[] BeforeKept, bool[] AfterKept) LongestCommonSubsequenceKeeps(
        string[] before,
        string[] after)
    {
        var lengths = new int[before.Length + 1, after.Length + 1];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            for (int j = after.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(before[i], after[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var beforeKept = new bool[before.Length];
        var afterKept = new bool[after.Length];
        int b = 0;
        int a = 0;
        while (b < before.Length && a < after.Length)
        {
            if (string.Equals(before[b], after[a], StringComparison.Ordinal))
            {
                beforeKept[b] = true;
                afterKept[a] = true;
                b++;
                a++;
            }
            else if (lengths[b + 1, a] >= lengths[b, a + 1])
            {
                b++;
            }
            else
            {
                a++;
            }
        }

        return (beforeKept, afterKept);
    }
}
