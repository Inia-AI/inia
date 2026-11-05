namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Base class for all output data stored in the Tables JSONB field.
/// Provides polymorphic storage for tables, charts, and future output types.
/// </summary>
public abstract class WorkflowOutputData
{
    /// <summary>
    /// The display name/title of the output data
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Discriminator property to identify the specific output type during deserialization
    /// </summary>
    public abstract string OutputType { get; }
}

/// <summary>
/// Represents chart output data for dynamic chart visualizations
/// </summary>
public class ChartOutput : WorkflowOutputData
{
    /// <summary>
    /// Identifies this as chart output data
    /// </summary>
    public override string OutputType => "Chart";

    /// <summary>
    /// The raw data array used to generate the chart
    /// </summary>
    public List<ChartDataPoint> ChartData { get; set; } = [];

    /// <summary>
    /// The complete chart configuration including type, axes, series, and display options
    /// </summary>
    public ChartConfiguration ChartConfig { get; set; } = new();
}
