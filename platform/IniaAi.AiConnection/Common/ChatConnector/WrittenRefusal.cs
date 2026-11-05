using System.Text.RegularExpressions;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// Finds a refusal the model wrote as its answer and ended normally, which no stop reason marks:
/// "I can't transcribe this one. The image is an identity card ...". The check is narrow on
/// purpose, because a real answer in this domain says "cannot", "passport", "personal" and
/// "policy" all the time. An answer counts as a refusal only when the whole answer is short, has
/// no figures, opens by declining the task, and either declines to transcribe or gives a reason
/// framed as the model's own (its guidelines, a person's personal data, privacy reasons).
/// </summary>
public static partial class WrittenRefusal
{
    private const int MAX_REFUSAL_LENGTH = 600;

    /// <summary>True when <paramref name="answer"/> is a written refusal and not an answer.</summary>
    public static bool IsRefusal(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        string text = answer.Trim();

        if (text.Length > MAX_REFUSAL_LENGTH || text.Any(char.IsDigit))
        {
            return false;
        }

        Match opening = DeclinesTheTask().Match(text);

        return opening.Success
            && (opening.Groups["transcribe"].Success || GivesARefusalReason().IsMatch(text));
    }

    /// <summary>A refusal as a stand-in for the answer, for a caller that found one.</summary>
    public static ModelRefusal ToRefusal(string answer, string? modelId)
    {
        return new ModelRefusal(ModelRefusal.WRITTEN_CATEGORY, Shorten(answer), null, modelId);
    }

    private static string Shorten(string answer)
    {
        string text = answer.Trim();

        return text.Length <= MAX_REFUSAL_LENGTH ? text : text[..MAX_REFUSAL_LENGTH];
    }

    [GeneratedRegex(
        @"^(?:(?:i'?m\s+)?sorry\s*[,—–-]?\s*(?:but\s+)?|i\s+apologi[sz]e\s*[,—–-]?\s*(?:but\s+)?|unfortunately\s*,?\s*)?i(?:\s+(?:can(?:'|’)?t|cannot|can\s+not|won(?:'|’)t|will\s+not|must\s+decline\s+to|need\s+to\s+decline\s+to|have\s+to\s+decline\s+to)|(?:'|’)m\s+(?:not\s+able|unable|not\s+going)\s+to|\s+am\s+(?:not\s+able|unable|not\s+going)\s+to)\s+(?:help\s+(?:with\s+)?|assist\s+(?:with\s+)?)?(?:(?<transcribe>transcrib)|extract|read|process|provid|shar|reproduc|compl|fulfil|help|assist|identify|analy[sz])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeclinesTheTask();

    [GeneratedRegex(
        @"\b(?:(?:against\s+)?my\s+(?:usage\s+|content\s+|safety\s+)?(?:guidelines|policies|principles)|(?:usage|content|safety)\s+polic(?:y|ies)|not\s+comfortable|personally\s+identifiable|for\s+privacy\s+reasons|privacy\s+(?:concerns|protections)|identity\s+(?:theft|fraud)|(?:a|an|the)\s+(?:real\s+|private\s+)?(?:person|individual)(?:'|’)s\s+(?:personal\s+)?(?:information|data|details|identity)|(?:a|an|the)\s+private\s+(?:person|individual)|personal\s+(?:information|data|details)\s+(?:of|about)\s+(?:a|an|this|the)\s+(?:real\s+|private\s+)?(?:person|individual)|(?:help|assist)\s+with\s+(?:that|this)(?:\s+request)?\s*[.!]?\s*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GivesARefusalReason();
}
