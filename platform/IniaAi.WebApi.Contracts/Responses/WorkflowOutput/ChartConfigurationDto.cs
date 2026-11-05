namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents the complete configuration for a dynamic chart.
/// </summary>
public class ChartConfigurationDto
{
    /// <summary>
    /// Gets or sets the chart type (line, bar, column, area, scatter, pie, donut, bubble).
    /// </summary>
    public string Type { get; set; } = "line";

    /// <summary>
    /// Gets or sets the chart title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw data array for the chart.
    /// </summary>
    public List<ChartDataPointDto> Data { get; set; } = [];

    /// <summary>
    /// Gets or sets the X-axis configuration.
    /// </summary>
    public ChartAxisDto XAxis { get; set; } = new();

    /// <summary>
    /// Gets or sets the Y-axis configuration.
    /// </summary>
    public ChartAxisDto YAxis { get; set; } = new();

    /// <summary>
    /// Gets or sets the chart series configuration.
    /// </summary>
    public List<ChartSeriesDto> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the chart display options.
    /// </summary>
    public ChartOptionsDto Options { get; set; } = new();

    /// <summary>
    /// Gets or sets the chart layout configuration.
    /// </summary>
    public ChartLayoutDto Layout { get; set; } = new();

    /// <summary>
    /// Gets or sets metadata about the chart.
    /// </summary>
    public ChartMetadataDto Metadata { get; set; } = new();
}
