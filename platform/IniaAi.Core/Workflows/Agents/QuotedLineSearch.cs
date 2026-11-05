using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// Deterministic check that a quoted line is printed in a page's text — the platform half of
/// the contract measured in the agent-runtime repo (evals/citation.ts): a quote matches when
/// it is a verbatim substring (whitespace aside) or when every quoted token appears in order
/// on ONE printed line, so column omissions and separator punctuation are tolerated while
/// invented tokens, reordering, and quotes stitched across lines still miss. Letter-free
/// digit-bearing tokens compare as sign plus digits — (1,514,705), -1,514,705 and -1514705
/// all assert one figure — while a flipped or dropped sign stays a miss, because the sign is
/// part of the figure. Tokens mixing letters and digits (FY2024, IFRS16) are labels, not
/// figures, and keep their exact spelling.
/// </summary>
public static partial class QuotedLineSearch
{
    private const char UNICODE_MINUS = '−';
    private const char EN_DASH_MINUS = '–';

    public static bool Matches(string? pageText, string quote)
    {
        if (string.IsNullOrWhiteSpace(pageText) || string.IsNullOrWhiteSpace(quote))
        {
            return false;
        }

        string needle = NormalizeWhitespace(quote);
        if (NormalizeWhitespace(pageText).Contains(needle, StringComparison.Ordinal)
            && FiguresAgreeInSign(pageText, quote))
        {
            return true;
        }

        List<string> needleTokens = QuotableTokensOf(quote);
        if (needleTokens.Count == 0)
        {
            return false;
        }

        return pageText
            .Split('\n')
            .Any(line => IsTokenSubsequence(needleTokens, QuotableTokensOf(line)));
    }

    private static string NormalizeWhitespace(string text)
    {
        return WhitespaceRun().Replace(text, " ").Trim();
    }

    private static List<string> QuotableTokensOf(string text)
    {
        return [.. WhitespaceRun()
            .Split(text)
            .Select(QuotableTokenOf)
            .Where(static token => token.Length > 0)];
    }

    private static string QuotableTokenOf(string token)
    {
        string? figure = CanonicalFigureOf(token);
        if (figure is not null)
        {
            return figure;
        }

        return token.Any(char.IsAsciiDigit)
            ? FigureEdgePunctuation().Replace(token, string.Empty)
            : WordEdgePunctuation().Replace(token, string.Empty);
    }

    private static string? CanonicalFigureOf(string token)
    {
        string trimmed = FigureEdgePunctuation().Replace(token, string.Empty);
        if (!trimmed.Any(char.IsAsciiDigit) || trimmed.Any(char.IsAsciiLetter))
        {
            return null;
        }

        string digits = new([.. trimmed.Where(char.IsAsciiDigit)]);
        if (digits.Length == 0)
        {
            return null;
        }

        bool negative = (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
            || trimmed.StartsWith('-')
            || trimmed.StartsWith(UNICODE_MINUS)
            || trimmed.StartsWith(EN_DASH_MINUS)
            || trimmed.EndsWith('-')
            || trimmed.EndsWith(UNICODE_MINUS)
            || trimmed.EndsWith(EN_DASH_MINUS);
        return negative ? $"-{digits}" : digits;
    }

    /// <summary>
    /// The substring fast-path sees "1,514,705" inside "(1,514,705)", so on its own it would
    /// hand a sign flip the acceptance the token path exists to refuse: a substring match also
    /// requires every canonical figure of the quote to appear, sign intact, in the searched text.
    /// </summary>
    private static bool FiguresAgreeInSign(string pageText, string quote)
    {
        List<string> quotedFigures = [.. WhitespaceRun()
            .Split(quote)
            .Select(CanonicalFigureOf)
            .OfType<string>()];
        if (quotedFigures.Count == 0)
        {
            return true;
        }

        var printedFigures = new HashSet<string>(WhitespaceRun()
            .Split(pageText)
            .Select(CanonicalFigureOf)
            .OfType<string>(), StringComparer.Ordinal);
        return quotedFigures.All(printedFigures.Contains);
    }

    private static bool IsTokenSubsequence(List<string> needle, List<string> hay)
    {
        int matched = 0;
        foreach (string token in hay)
        {
            if (!string.Equals(token, needle[matched], StringComparison.Ordinal))
            {
                continue;
            }

            matched += 1;
            if (matched == needle.Count)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"^[^0-9A-Za-z()−–-]+|[^0-9A-Za-z()−–-]+$")]
    private static partial Regex FigureEdgePunctuation();

    [GeneratedRegex("^[^0-9A-Za-z]+|[^0-9A-Za-z]+$")]
    private static partial Regex WordEdgePunctuation();
}
