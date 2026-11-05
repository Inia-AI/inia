using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.ReportFeedback;

/// <summary>
/// Request to leave feedback on a report. The item belongs to the report as a whole; the
/// reader's text selection, when any, is kept as quoted text.
/// </summary>
public record CreateReportFeedbackRequest
{
    /// <summary>
    /// Gets the text the reader had selected in the report, when any.
    /// </summary>
    public string? QuotedText { get; init; }

    /// <summary>
    /// Gets the category of the problem.
    /// </summary>
    public ReportFeedbackCategoryDto Category { get; init; }

    /// <summary>
    /// Gets the reader's comment.
    /// </summary>
    public string Comment { get; init; } = string.Empty;
}
