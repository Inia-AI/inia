namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Defines the layout configuration for report generation (Word/PDF export).
/// Currently supports page orientation and page size, with room for future enhancements.
/// </summary>
public class ReportLayout
{
    /// <summary>
    /// Page orientation: "Portrait" or "Landscape"
    /// </summary>
    public string Orientation { get; set; } = "Portrait";

    /// <summary>
    /// Page size: "A4", "Letter", etc.
    /// </summary>
    public string PageSize { get; set; } = "A4";

    /// <summary>
    /// Version of the layout configuration for future compatibility
    /// </summary>
    public string Version { get; set; } = "1.0";
}
