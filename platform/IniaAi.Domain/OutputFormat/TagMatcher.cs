using System.Text.RegularExpressions;

namespace IniaAi.Domain.OutputFormat;

/// <summary>
/// Shared tag-matching helpers. v1 supports plain tag names only, no attributes,
/// no nesting. Tag names must match a strict identifier pattern to avoid regex injection.
/// </summary>
public static partial class TagMatcher
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.Compiled)]
    private static partial Regex TagNamePattern();

    public static bool IsValidTagName(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName))
        {
            return false;
        }

        return TagNamePattern().IsMatch(tagName);
    }

    /// <summary>
    /// Returns the inner content of the first occurrence of &lt;tagName&gt;...&lt;/tagName&gt;
    /// in <paramref name="text"/>, or null if no match. Empty inner content is returned as
    /// an empty string (counts as a successful match).
    /// </summary>
    public static string? FindFirstTagContent(string text, string tagName)
    {
        if (!IsValidTagName(tagName))
        {
            return null;
        }

        string pattern = $"<{Regex.Escape(tagName)}>([\\s\\S]*?)</{Regex.Escape(tagName)}>";
        Match match = Regex.Match(text, pattern);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Value;
    }

    public static IReadOnlyList<string> SplitTagNames(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
