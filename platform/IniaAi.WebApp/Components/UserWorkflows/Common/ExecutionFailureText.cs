using System.Text;

namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// Turns a persisted failure (kind + detail) into text a list row can hold. The detail is
/// system-written prose up to
/// <see cref="IniaAi.Domain.Entities.WorkflowExecutions.WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH"/>
/// characters and may carry newlines, so the row shows a collapsed, length-capped
/// <see cref="Summary"/> and the disclosure shows <see cref="Full"/> verbatim.
/// </summary>
public static class ExecutionFailureText
{
    /// <summary>How much of the detail a single grid row is allowed to spend.</summary>
    public const int SUMMARY_MAX_LENGTH = 140;

    /// <summary>Shown in place of a kind the engine could not classify.</summary>
    public const string UNCLASSIFIED_KIND = "Unclassified";

    private const char ELLIPSIS = '…';

    /// <summary>How far back from the cut a word boundary is still worth honouring.</summary>
    private const int WORD_BOUNDARY_LOOKBACK = 20;

    /// <summary>Whether either persisted failure column carries anything to show.</summary>
    public static bool HasFailure(string? failureKind, string? failureDetail)
    {
        return !string.IsNullOrWhiteSpace(failureKind) || !string.IsNullOrWhiteSpace(failureDetail);
    }

    /// <summary>
    /// The failure kind for display, falling back to <see cref="UNCLASSIFIED_KIND"/> so a failed
    /// row whose cause the engine could not classify is still labelled.
    /// </summary>
    public static string Kind(string? failureKind)
    {
        return string.IsNullOrWhiteSpace(failureKind) ? UNCLASSIFIED_KIND : failureKind.Trim();
    }

    /// <summary>
    /// The detail collapsed to a single line and capped at <see cref="SUMMARY_MAX_LENGTH"/>
    /// characters, so no failure text can stretch a list row. Empty when there is no detail.
    /// </summary>
    public static string Summary(string? failureDetail)
    {
        string collapsed = Collapse(failureDetail);
        if (collapsed.Length <= SUMMARY_MAX_LENGTH)
        {
            return collapsed;
        }

        int cut = SUMMARY_MAX_LENGTH;
        int lastSpace = collapsed.LastIndexOf(' ', cut - 1);
        if (lastSpace >= cut - WORD_BOUNDARY_LOOKBACK)
        {
            cut = lastSpace;
        }

        return collapsed.AsSpan(0, cut).TrimEnd().ToString() + ELLIPSIS;
    }

    /// <summary>
    /// Whether <see cref="Summary"/> dropped text, so the row must offer the disclosure rather
    /// than pretend the summary is the whole story.
    /// </summary>
    public static bool IsTruncated(string? failureDetail)
    {
        return Collapse(failureDetail).Length > SUMMARY_MAX_LENGTH;
    }

    /// <summary>
    /// The whole failure for the disclosure: the kind, then the detail exactly as persisted —
    /// newlines and all. Either half is omitted when absent.
    /// </summary>
    public static string Full(string? failureKind, string? failureDetail)
    {
        bool hasKind = !string.IsNullOrWhiteSpace(failureKind);
        bool hasDetail = !string.IsNullOrWhiteSpace(failureDetail);

        if (hasKind && hasDetail)
        {
            return $"{failureKind!.Trim()}: {failureDetail!.Trim()}";
        }

        if (hasKind)
        {
            return failureKind!.Trim();
        }

        return hasDetail ? failureDetail!.Trim() : string.Empty;
    }

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                _ = builder.Append(' ');
                pendingSpace = false;
            }

            _ = builder.Append(character);
        }

        return builder.ToString();
    }
}
