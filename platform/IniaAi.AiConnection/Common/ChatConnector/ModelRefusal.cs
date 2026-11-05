using IniaAi.AiConnection.Abstractions;

namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// The model declined a request. The provider says so with <c>stop_reason: "refusal"</c> and,
/// on current models, a <c>stop_details</c> object that names the policy category. A model can
/// also refuse in plain words and end its turn normally; <see cref="WrittenRefusal"/> finds those.
/// </summary>
/// <param name="Category">The provider's refusal category, such as <c>reasoning_extraction</c>,
/// <see cref="UNSPECIFIED_CATEGORY"/> when the provider named none, or
/// <see cref="WRITTEN_CATEGORY"/> for a refusal written as the answer.</param>
/// <param name="Explanation">The provider's explanation, when it gave one.</param>
/// <param name="RequestId">The provider's id for the refused message, for a support request.</param>
/// <param name="ModelId">The model id the request was sent to.</param>
public sealed record ModelRefusal(string Category, string? Explanation, string? RequestId, string? ModelId)
{
    public const string UNSPECIFIED_CATEGORY = "unspecified";

    public const string WRITTEN_CATEGORY = "written";

    /// <summary>
    /// The fixed words every refusal log line starts with. The refusal alarm counts the lines
    /// that carry them, so they do not change.
    /// </summary>
    public const string LOG_MARKER = "Model refusal";

    /// <summary>How a refusal reads where an answer would have been.</summary>
    public const string ANSWER_PREFIX = IChatConnector.ERROR_PREFIX + "The model refused to answer";

    private const int MAX_CATEGORY_LENGTH = 64;

    /// <summary>The text that stands in for the answer: an error, so no caller reads it as one.</summary>
    public string ToAnswerText()
    {
        string explanation = string.IsNullOrWhiteSpace(Explanation)
            ? string.Empty
            : $" {Explanation.Trim()}";

        return $"{ANSWER_PREFIX} (refusal category: {Category}).{explanation}";
    }

    /// <summary>The category as a stored value: trimmed, bounded and never empty.</summary>
    public static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return UNSPECIFIED_CATEGORY;
        }

        string trimmed = category.Trim();

        return trimmed.Length > MAX_CATEGORY_LENGTH
            ? trimmed[..MAX_CATEGORY_LENGTH]
            : trimmed;
    }

    /// <summary>True when <paramref name="text"/> is the stand-in a refusal leaves in place of an answer.</summary>
    public static bool IsAnswerText(string? text)
    {
        return text?.StartsWith(ANSWER_PREFIX, StringComparison.Ordinal) == true;
    }
}
