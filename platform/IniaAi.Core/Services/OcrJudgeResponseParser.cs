using IniaAi.Common;

namespace IniaAi.Core.Services;

internal readonly record struct OcrJudgeVerdict(bool QualityBad, bool VerdictPresent);

internal static class OcrJudgeResponseParser
{
    private const string QUALITY_TAG = "ocr_quality";
    private const string BAD_VERDICT = "bad";

    private static readonly char[] VERDICT_SEPARATORS =
        [' ', '\t', '\n', '\r', '.', ',', ';', ':', '-', '(', ')', '!', '?', '"', '\''];

    private static readonly string[] NEGATION_TOKENS = ["not", "no", "never"];

    public static OcrJudgeVerdict Parse(string? response)
    {
        string? quality = Utils.GetStringFromLastTag(response, QUALITY_TAG);
        bool verdictPresent = !string.IsNullOrWhiteSpace(quality);
        bool qualityBad = ContainsBadVerdict(quality);

        return new OcrJudgeVerdict(qualityBad, verdictPresent);
    }

    private static bool ContainsBadVerdict(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return false;
        }

        string[] tokens = quality.Split(VERDICT_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!string.Equals(tokens[i], BAD_VERDICT, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool negated = i > 0
                && NEGATION_TOKENS.Contains(tokens[i - 1], StringComparer.OrdinalIgnoreCase);

            if (!negated)
            {
                return true;
            }
        }

        return false;
    }
}
