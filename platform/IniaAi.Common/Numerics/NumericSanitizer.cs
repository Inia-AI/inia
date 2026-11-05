using System.Globalization;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Numerics;

/// <summary>
/// Extracts clean numeric values from noisy strings (e.g. LLM output like "page17", "$500").
/// </summary>
public static partial class NumericSanitizer
{
    /// <summary>
    /// Extracts the first integer from a messy string.
    /// Returns null if no number is found or the input is null/empty.
    /// Examples: "page17" → 17, "Page 17" → 17, "$500" → 500, "3.7" → 3.
    /// </summary>
    public static int? ExtractInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        Match match = NumberPattern().Match(input);
        if (!match.Success)
        {
            return null;
        }

        string value = match.Value;

        // Truncate at decimal point for int extraction
        int dotIndex = value.IndexOf('.');
        if (dotIndex >= 0)
        {
            value = value[..dotIndex];
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Extracts the first number (including decimals) from a messy string.
    /// Returns null if no number is found or the input is null/empty.
    /// Examples: "page 2.5" → 2.5, "$99.99" → 99.99.
    /// </summary>
    public static double? ExtractDouble(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        Match match = NumberPattern().Match(input);
        if (!match.Success)
        {
            return null;
        }

        if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Extracts a list of integers from a string containing numbers, comma-separated lists,
    /// and ranges (e.g. "7,12-15,18" → [7,12,13,14,15,18]). Handles all common dash variants
    /// and LLM noise in each segment. Returns a sorted, deduplicated list.
    /// </summary>
    public static List<int> ExtractIntList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        string normalised = NormaliseDashes(input);

        var results = new SortedSet<int>();
        string[] segments = normalised.Split(',');

        foreach (string segment in segments)
        {
            string trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // Check if this segment contains a range (dash between two numbers).
            // We need to find a dash that separates two number-containing parts,
            // not just a negative sign at the start.
            int dashIndex = FindRangeDashIndex(trimmed);

            if (dashIndex >= 0)
            {
                string leftPart = trimmed[..dashIndex];
                string rightPart = trimmed[(dashIndex + 1)..];

                int? start = ExtractInt(leftPart);
                int? end = ExtractInt(rightPart);

                if (start is not null && end is not null)
                {
                    int lo = Math.Min(start.Value, end.Value);
                    int hi = Math.Max(start.Value, end.Value);

                    for (int i = lo; i <= hi; i++)
                    {
                        _ = results.Add(i);
                    }

                    continue;
                }
            }

            // Not a range — try as a single value
            int? value = ExtractInt(trimmed);
            if (value is not null)
            {
                _ = results.Add(value.Value);
            }
        }

        return [.. results];
    }

    /// <summary>
    /// Finds the index of a dash that acts as a range separator (not a negative sign).
    /// Returns -1 if no range dash is found.
    /// </summary>
    private static int FindRangeDashIndex(string segment)
    {
        // Look for a dash that has a digit somewhere before it and a digit somewhere after it.
        for (int i = 1; i < segment.Length - 1; i++)
        {
            if (segment[i] == '-')
            {
                bool hasDigitBefore = segment[..i].Any(char.IsDigit);
                bool hasDigitAfter = segment[(i + 1)..].Any(char.IsDigit);

                if (hasDigitBefore && hasDigitAfter)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Replaces all common dash/hyphen Unicode variants with a standard hyphen-minus (U+002D).
    /// Covers en-dash, em-dash, minus sign, Unicode hyphen, and non-breaking hyphen.
    /// </summary>
    public static string NormaliseDashes(string input)
    {
        return input
            .Replace('\u2013', '-') // en-dash
            .Replace('\u2014', '-') // em-dash
            .Replace('\u2212', '-') // minus sign
            .Replace('\u2010', '-') // unicode hyphen
            .Replace('\u2011', '-'); // non-breaking hyphen
    }

    /// <summary>
    /// Strips characters that commonly wrap a numeric list but aren't part of it:
    /// brackets ([](){}), quotes ("'), and the word "page"/"pages" (case-insensitive).
    /// Anything else is left untouched so the strict parser can reject malformed input.
    /// </summary>
    public static string StripCommonListNoise(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        string stripped = ListNoiseCharsPattern().Replace(input, "");
        return PageWordPattern().Replace(stripped, "");
    }

    /// <summary>
    /// Matches the first number in a string: optional sign, digits, optional decimal portion.
    /// </summary>
    [GeneratedRegex(@"[+-]?\d+\.?\d*")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[\[\]\(\)\{\}""']")]
    private static partial Regex ListNoiseCharsPattern();

    [GeneratedRegex(@"pages?", RegexOptions.IgnoreCase)]
    private static partial Regex PageWordPattern();
}
