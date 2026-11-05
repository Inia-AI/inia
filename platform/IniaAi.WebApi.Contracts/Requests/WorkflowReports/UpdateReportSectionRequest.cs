namespace IniaAi.WebApi.Contracts.Requests.WorkflowReports;

/// <summary>
/// Request to update the editable fields of a report section.
/// </summary>
public record UpdateReportSectionRequest
{
    /// <summary>
    /// Gets the section title.
    /// </summary>
    public string? Title { get; init; }
}
