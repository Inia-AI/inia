namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents metadata about a chart.
/// </summary>
public class ChartMetadata
{
    /// <summary>
    /// Gets or sets the field mappings for the chart.
    /// </summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of data points in the chart.
    /// </summary>
    public int DataPoints { get; set; }

    /// <summary>
    /// Gets or sets when the chart was generated.
    /// </summary>
    public DateTime? GeneratedAt { get; set; }

    /// <summary>
    /// Gets or sets the currency for financial data.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets the unit for the data values.
    /// </summary>
    public string? Unit { get; set; }
}
