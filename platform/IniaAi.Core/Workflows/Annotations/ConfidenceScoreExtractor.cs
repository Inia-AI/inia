using System.Globalization;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations;

public interface IConfidenceScoreExtractor
{
    double? ExtractConfidenceScore(string rawResponse);
}

public class ConfidenceScoreExtractor : IConfidenceScoreExtractor
{
    public double? ExtractConfidenceScore(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
        {
            return null;
        }

        // Regular expression to match a number inside <confidence>...</confidence>
        string pattern = @"<confidence>(?<score>[0-9]*\.?[0-9]+)</confidence>";
        Match match = Regex.Match(rawResponse, pattern);
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
}
