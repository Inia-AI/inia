namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents a data series in a chart.
/// </summary>
public class ChartSeries
{
    /// <summary>
    /// Gets or sets the series name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data points for this series.
    /// </summary>
    public List<ChartSeriesDataPoint> Data { get; set; } = [];

    /// <summary>
    /// Gets or sets the color for this series.
    /// </summary>
    public string? Color { get; set; }
}

/// <summary>
/// Represents a data point within a series.
/// </summary>
public class ChartSeriesDataPoint
{
    /// <summary>
    /// Gets or sets the X value.
    /// </summary>
    public object X { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Y value.
    /// </summary>
    public double Y { get; set; }
}
