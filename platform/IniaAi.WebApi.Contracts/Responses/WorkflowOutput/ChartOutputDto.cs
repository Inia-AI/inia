namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a chart output generated as part of a workflow block's result.
/// Contains chart data and configuration for dynamic chart visualization.
/// </summary>
public class ChartOutputDto : WorkflowOutputDataDto
{
    /// <summary>
    /// Identifies this as chart output data.
    /// </summary>
    public override string OutputType => "Chart";

    /// <summary>
    /// Gets or sets the raw data array used to generate the chart.
    /// </summary>
    public List<ChartDataPointDto> ChartData { get; set; } = [];

    /// <summary>
    /// Gets or sets the complete chart configuration including type, axes, series, and display options.
    /// </summary>
    public ChartConfigurationDto ChartConfig { get; set; } = new();
}
