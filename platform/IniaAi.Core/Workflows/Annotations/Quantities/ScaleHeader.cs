using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// A column or page header that names the scale a statement prints its amounts in: "£'000",
/// "US$'000", "€000", "£k", "$m", "CHF millions", "$bn". Its zeros are part of the header, never
/// an amount.
/// </summary>
internal static partial class ScaleHeader
{
    private const string ZEROS = "000";

    public static MatchCollection Matches(string text)
    {
        return HeaderRegex().Matches(text);
    }

    /// <summary>The spans of every thousands header written in zeros, whose "000" a number pass would read as 0.</summary>
    public static List<(int Start, int End)> ZeroHeaderSpans(string text)
    {
        if (!text.Contains(ZEROS, StringComparison.Ordinal))
        {
            return [];
        }

        return [.. HeaderRegex().Matches(text)
            .Where(match => match.Groups["scale"].Value.StartsWith('0'))
            .Select(match => (match.Index, match.Index + match.Length))];
    }

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?<currency>US\$|£|\$|€|GBP|USD|EUR|CHF|JPY|CAD|AUD|NZD|SEK|NOK|DKK|HKD|SGD|CNY|INR|ZAR)\s?['’]?\s?(?<scale>000s?|thousands?|millions?|billions?|mn|bn|m|k)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();
}
