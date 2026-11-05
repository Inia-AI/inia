namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents axis configuration for a chart.
/// </summary>
public class ChartAxisDto
{
    /// <summary>
    /// Gets or sets the axis type (linear, logarithmic, category, datetime).
    /// </summary>
    public string Type { get; set; } = "linear";

    /// <summary>
    /// Gets or sets the field name in the data that this axis represents.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display label for the axis.
    /// </summary>
    public string Label { get; set; } = string.Empty;
}