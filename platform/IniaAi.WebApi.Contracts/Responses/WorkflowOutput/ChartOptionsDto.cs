using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents display options for a chart.
/// </summary>
public class ChartOptionsDto
{
    /// <summary>
    /// Gets or sets whether the chart should be responsive.
    /// </summary>
    public bool Responsive { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show the legend.
    /// </summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>
    /// Gets or sets the color scheme (default, professional, vibrant, earth).
    /// </summary>
    public string ColorScheme { get; set; } = "default";

    /// <summary>
    /// Gets or sets whether to show grid lines.
    /// </summary>
    public bool ShowGridLines { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable animations.
    /// </summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to maintain aspect ratio.
    /// </summary>
    public bool MaintainAspectRatio { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable chart export functionality.
    /// </summary>
    public bool? EnableExport { get; set; }

    /// <summary>
    /// Gets or sets the series group field for grouped charts.
    /// </summary>
    public string? SeriesGroupField { get; set; }

    /// <summary>
    /// Gets or sets the stacking mode for bar and column charts.
    /// Null for chart types that don't support stacking.
    /// </summary>
    public ChartStackingMode? Stacking { get; set; }
}