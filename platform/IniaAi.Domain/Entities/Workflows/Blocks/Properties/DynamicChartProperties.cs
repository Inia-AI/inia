using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for dynamic chart generation block.
/// </summary>
public class DynamicChartProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the JSON data source. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.DataSourceJson"/> for
    /// the actual data source after replacements.
    /// </summary>
    [JsonProperty(nameof(DataSourceJson), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? DataSourceJson { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the chart type. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.ChartType"/> for the
    /// actual chart type after replacements.
    /// </summary>
    /// <remarks>
    /// Valid values: line, bar, column, pie, etc.
    /// </remarks>
    [JsonProperty(nameof(ChartType))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ChartType { get; set; } = "line";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the X-axis field. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.XAxisField"/> for the
    /// actual X-axis field after replacements.
    /// </summary>
    [JsonProperty(nameof(XAxisField), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? XAxisField { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the Y-axis field. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.YAxisField"/> for the
    /// actual Y-axis field after replacements.
    /// </summary>
    [JsonProperty(nameof(YAxisField), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? YAxisField { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the series-group field. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.SeriesGroupField"/>
    /// for the actual series-group field after replacements.
    /// </summary>
    [JsonProperty(nameof(SeriesGroupField), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? SeriesGroupField { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the chart title. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.ChartTitle"/> for the
    /// actual chart title after replacements.
    /// </summary>
    [JsonProperty(nameof(ChartTitle))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ChartTitle { get; set; } = "Dynamic Chart";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the X-axis label. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.XAxisLabel"/> for the
    /// actual X-axis label after replacements.
    /// </summary>
    [JsonProperty(nameof(XAxisLabel))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string XAxisLabel { get; set; } = "X Axis";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the Y-axis label. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.YAxisLabel"/> for the
    /// actual Y-axis label after replacements.
    /// </summary>
    [JsonProperty(nameof(YAxisLabel))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string YAxisLabel { get; set; } = "Y Axis";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the color scheme. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.ColorScheme"/> for
    /// the actual color scheme after replacements.
    /// </summary>
    /// <remarks>
    /// Valid values: default, professional, vibrant, earth.
    /// </remarks>
    [JsonProperty(nameof(ColorScheme))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ColorScheme { get; set; } = "default";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the chart height. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.ChartHeight"/> for
    /// the actual chart height after replacements.
    /// </summary>
    /// <remarks>
    /// CSS-compatible length string (e.g., "400", "500px").
    /// </remarks>
    [JsonProperty(nameof(ChartHeight))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ChartHeight { get; set; } = "400";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the chart width. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.ChartWidth"/> for the
    /// actual chart width after replacements.
    /// </summary>
    /// <remarks>
    /// CSS-compatible length string (e.g., "100%", "800px").
    /// </remarks>
    [JsonProperty(nameof(ChartWidth))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ChartWidth { get; set; } = "100%";

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the data filters. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.DataFilters"/> for
    /// the actual data filters after replacements.
    /// </summary>
    /// <remarks>
    /// JSON array of filter objects to apply to the data source.
    /// </remarks>
    [JsonProperty(nameof(DataFilters))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string DataFilters { get; set; } = "[]";

    /// <summary>
    /// Whether to show legend.
    /// </summary>
    [JsonProperty(nameof(ShowLegend))]
    public bool ShowLegend { get; set; } = true;

    /// <summary>
    /// Whether to show grid lines.
    /// </summary>
    [JsonProperty(nameof(ShowGridLines))]
    public bool ShowGridLines { get; set; } = true;

    /// <summary>
    /// Whether to enable animations.
    /// </summary>
    [JsonProperty(nameof(EnableAnimations))]
    public bool EnableAnimations { get; set; } = true;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the stacking mode. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="DynamicChartPropertiesExecutionState.Stacking"/> for the
    /// actual stacking mode after replacements.
    /// </summary>
    /// <remarks>
    /// Bar/column stacking mode. Valid values: none, normal, percent.
    /// </remarks>
    [JsonProperty(nameof(Stacking))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string Stacking { get; set; } = "none";

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public DynamicChartPropertiesExecutionState? ExecutionState { get; set; }

    public DynamicChartProperties()
    {
        Type = BlockTypes.DynamicChart;
    }

    public override BlockProperties Clone()
    {
        var clone = new DynamicChartProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new DynamicChartPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new DynamicChartPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
