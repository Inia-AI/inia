namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents layout configuration for a chart.
/// </summary>
public class ChartLayout
{
    /// <summary>
    /// Gets or sets the chart width (can be percentage or pixel value).
    /// </summary>
    public string Width { get; set; } = "100%";

    /// <summary>
    /// Gets or sets the chart height (can be percentage or pixel value).
    /// </summary>
    public string Height { get; set; } = "400";
}