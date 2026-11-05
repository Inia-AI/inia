namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents a single data point in a chart.
/// </summary>
public class ChartDataPoint
{
    /// <summary>
    /// Gets or sets the X-axis value (typically time/category).
    /// </summary>
    public string X { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Y-axis value (typically numeric value).
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the series name this point belongs to.
    /// </summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw data associated with this point.
    /// </summary>
    public Dictionary<string, object>? RawData { get; set; }
}
