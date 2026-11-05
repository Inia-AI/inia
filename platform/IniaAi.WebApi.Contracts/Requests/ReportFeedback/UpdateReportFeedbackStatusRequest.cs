using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.ReportFeedback;

/// <summary>
/// Request to move a feedback item to a new status. Editor roles only.
/// </summary>
public record UpdateReportFeedbackStatusRequest
{
    /// <summary>
    /// Gets the status to move the item to.
    /// </summary>
    public ReportFeedbackStatusDto Status { get; init; }

    /// <summary>
    /// Gets the short reason for dismissing the item. Required when
    /// <see cref="Status"/> is <see cref="ReportFeedbackStatusDto.Dismissed"/>, ignored otherwise.
    /// </summary>
    public string? DismissReason { get; init; }
}
