using System.Globalization;
using System.Text.RegularExpressions;
using IniaAi.Core.Common;

namespace IniaAi.Core.Workflows.Annotations;

public interface IConfidenceScoreExtractor
{
    double? ExtractConfidenceScore(string rawResponse);
}

public partial class ConfidenceScoreExtractor : IConfidenceScoreExtractor
{
    public double? ExtractConfidenceScore(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
        {
            return null;
        }

        Match match = ConfidencePattern().Match(rawResponse);
        if (match.Success)
        {
            string scoreString = match.Groups["score"].Value;
            if (double.TryParse(scoreString, NumberStyles.Any, CultureInfo.InvariantCulture, out double score))
            {
                return score;
            }
        }

        return null;
    }

    [GeneratedRegex(@"<confidence>(?<score>[0-9]*\.?[0-9]+)</confidence>", RegexOptions.None, matchTimeoutMilliseconds: RegexReplacer.REGEX_TIMEOUT_MILLISECONDS)]
    private static partial Regex ConfidencePattern();
}
