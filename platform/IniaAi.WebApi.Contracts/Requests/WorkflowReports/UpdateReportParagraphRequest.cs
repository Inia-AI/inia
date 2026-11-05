namespace IniaAi.WebApi.Contracts.Requests.WorkflowReports;

/// <summary>
/// Request to update the editable fields of a report paragraph.
/// </summary>
public record UpdateReportParagraphRequest
{
    /// <summary>
    /// Gets the paragraph title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the paragraph subtitle.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Gets the confidence score for the paragraph.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Gets the paragraph content.
    /// </summary>
    public string? Content { get; init; }
}
