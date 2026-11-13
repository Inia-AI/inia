using System.Text.RegularExpressions;

namespace IniaAi.Core.Common;

/// <summary>
/// Shared find/replace used by string-manipulation-style blocks (e.g. StringManipulation,
/// MergeJson pre-parse transforms). Owns the visible Zapier-style whitespace tokens and the ReDoS
/// timeout so every block treats user-supplied find/replace identically.
/// </summary>
public static class RegexReplacer
{
    // Visible, unambiguous tokens — Zapier-style. Substituted in find/replace strings so authors
    // never type raw backslashes; they type visible tokens that mean exactly what they say.
    public const string TOKEN_NEWLINE = "[:newline:]";
    public const string TOKEN_TAB = "[:tab:]";
    public const string TOKEN_SPACE = "[:space:]";
    public const string TOKEN_RETURN = "[:return:]";

    // Guards user-supplied regex from catastrophic backtracking hanging a worker thread.
    public const int REGEX_TIMEOUT_MILLISECONDS = 1_000;

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(REGEX_TIMEOUT_MILLISECONDS);

    /// <summary>
    /// Every TOKEN_NEWLINE / TOKEN_TAB / TOKEN_SPACE / TOKEN_RETURN becomes its actual character.
    /// </summary>
    public static string NormalizeTokens(string value)
    {
        return value
            .Replace(TOKEN_NEWLINE, "\n")
            .Replace(TOKEN_TAB, "\t")
            .Replace(TOKEN_SPACE, " ")
            .Replace(TOKEN_RETURN, "\r");
    }

    /// <summary>
    /// Find/replace on <paramref name="input"/>. Whitespace tokens in <paramref name="find"/> and
    /// <paramref name="replaceWith"/> are normalized first. An empty find leaves the input unchanged.
    /// When <paramref name="isRegex"/> is true, <paramref name="find"/> is a .NET regex (capture groups
    /// via <c>$1</c> etc.) run under a 1s timeout; otherwise it is a literal substring.
    /// Throws <see cref="ArgumentException"/> on an invalid pattern and
    /// <see cref="RegexMatchTimeoutException"/> on catastrophic backtracking — callers decide handling.
    /// </summary>
    public static string Replace(string input, string find, string replaceWith, bool isRegex, bool caseSensitive)
    {
        find = NormalizeTokens(find);
        replaceWith = NormalizeTokens(replaceWith);

        if (find.Length == 0)
        {
            return input;
        }

        if (!isRegex)
        {
            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return input.Replace(find, replaceWith, comparison);
        }

        RegexOptions options = (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
            | RegexOptions.CultureInvariant;
        return Regex.Replace(input, find, replaceWith, options, RegexTimeout);
    }
}
