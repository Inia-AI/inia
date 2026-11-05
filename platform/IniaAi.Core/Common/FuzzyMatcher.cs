using System.Text.RegularExpressions;

namespace IniaAi.Core.Common;

public static partial class FuzzyMatcher
{
    public static double CalculateSimilarity(string? source, string? target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return 0.0;
        }

        source = NormalizeString(source);
        target = NormalizeString(target);

        if (source == target)
        {
            return 1.0;
        }

        int distance = LevenshteinDistance(source, target);
        int maxLength = Math.Max(source.Length, target.Length);

        return 1.0 - ((double)distance / maxLength);
    }

    public static int LevenshteinDistance(string source, string target)
    {
        int[] previous = new int[target.Length + 1];
        int[] current = new int[target.Length + 1];

        for (int j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= source.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= target.Length; j++)
            {
                int substitution = previous[j - 1] + (source[i - 1] == target[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }

    private static string NormalizeString(string input)
    {
        // Remove special characters and extra spaces
        input = GenerateNonWordCharacterRegex().Replace(input.ToLowerInvariant(), "");
        input = GenerateWhitespaceRegex().Replace(input, " ").Trim();

        // Remove common company suffixes
        input = GenerateCompanyTypeRegex().Replace(input, "");
        return input.Trim();
    }

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex GenerateNonWordCharacterRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex GenerateWhitespaceRegex();

    [GeneratedRegex(@"\b(llc|ltd|inc|corp|limited|capital management|capital|management|holdings|group)\b")]
    private static partial Regex GenerateCompanyTypeRegex();
}
