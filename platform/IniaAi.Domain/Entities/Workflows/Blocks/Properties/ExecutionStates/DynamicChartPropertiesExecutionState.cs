using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for DynamicChartProperties. Holds resolved DataSourceJson.
/// </summary>
public class DynamicChartPropertiesExecutionState : BlockPropertiesExecutionStateBase<DynamicChartPropertiesExecutionState>
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(DataSourceJson), NullValueHandling = NullValueHandling.Ignore)]
    public string? DataSourceJson { get; set; }

    [JsonProperty(nameof(ChartType), NullValueHandling = NullValueHandling.Ignore)]
    public string? ChartType { get; set; }

    [JsonProperty(nameof(XAxisField), NullValueHandling = NullValueHandling.Ignore)]
    public string? XAxisField { get; set; }

    [JsonProperty(nameof(YAxisField), NullValueHandling = NullValueHandling.Ignore)]
    public string? YAxisField { get; set; }

    [JsonProperty(nameof(SeriesGroupField), NullValueHandling = NullValueHandling.Ignore)]
    public string? SeriesGroupField { get; set; }

    [JsonProperty(nameof(ChartTitle), NullValueHandling = NullValueHandling.Ignore)]
    public string? ChartTitle { get; set; }

    [JsonProperty(nameof(XAxisLabel), NullValueHandling = NullValueHandling.Ignore)]
    public string? XAxisLabel { get; set; }

    [JsonProperty(nameof(YAxisLabel), NullValueHandling = NullValueHandling.Ignore)]
    public string? YAxisLabel { get; set; }

    [JsonProperty(nameof(ColorScheme), NullValueHandling = NullValueHandling.Ignore)]
    public string? ColorScheme { get; set; }

    [JsonProperty(nameof(ChartHeight), NullValueHandling = NullValueHandling.Ignore)]
    public string? ChartHeight { get; set; }

    [JsonProperty(nameof(ChartWidth), NullValueHandling = NullValueHandling.Ignore)]
    public string? ChartWidth { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(DataFilters), NullValueHandling = NullValueHandling.Ignore)]
    public string? DataFilters { get; set; }

    [JsonProperty(nameof(Stacking), NullValueHandling = NullValueHandling.Ignore)]
    public string? Stacking { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal DynamicChartPropertiesExecutionState()
    {
    }

    public DynamicChartPropertiesExecutionState(DynamicChartProperties source)
    {
        DataSourceJson = source.DataSourceJson;
        ChartType = source.ChartType;
        XAxisField = source.XAxisField;
        YAxisField = source.YAxisField;
        SeriesGroupField = source.SeriesGroupField;
        ChartTitle = source.ChartTitle;
        XAxisLabel = source.XAxisLabel;
        YAxisLabel = source.YAxisLabel;
        ColorScheme = source.ColorScheme;
        ChartHeight = source.ChartHeight;
        ChartWidth = source.ChartWidth;
        DataFilters = source.DataFilters;
        Stacking = source.Stacking;
    }
}
