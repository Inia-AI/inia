using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums.Charts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// Strategy for creating dynamic charts from JSON data with configurable axes and chart types
/// </summary>
public class DynamicChartStrategy : IBlockStrategy
{
    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        // Initialize execution state
        block.ExecutionState ??= new BlockExecutionState(block);

        try
        {
            DynamicChartProperties props = block.TryGetProperties<DynamicChartProperties>()
                ?? throw new InvalidOperationException($"Block properties are not of type {nameof(DynamicChartProperties)}");

            // Templated fields (DataSourceJson, axis fields, labels, etc.) are
            // resolved into ExecutionState by ReplaceAllPlaceholders before the
            // strategy runs. Read from ExecutionState so placeholders like
            // {Block.answer} take effect at runtime.
            props.ExecutionState ??= new(props);
            DynamicChartPropertiesExecutionState state = props.ExecutionState;

            string dataSourceJson = state.DataSourceJson
                ?? throw new InvalidOperationException("Data source JSON is required for dynamic chart generation");
            string xAxisField = state.XAxisField
                ?? throw new InvalidOperationException("X-axis field is required for dynamic chart generation");
            string yAxisField = state.YAxisField
                ?? throw new InvalidOperationException("Y-axis field is required for dynamic chart generation");
            string chartType = state.ChartType ?? props.ChartType;

            context.Logger.LogInformation(
                "{block} Generating dynamic chart - Type: {ChartType}, X: {XAxis}, Y: {YAxis}",
                block, chartType, xAxisField, yAxisField);

            // Parse and process the data
            List<JObject> parsedData = ParseDataSource(dataSourceJson);
            List<JObject> filteredData = ApplyDataFilters(parsedData, state.DataFilters ?? props.DataFilters);
            bool isCritical = block.AbortsExecutionOnFailure();
            ChartPoints points = ProcessDataForChart(
                filteredData, xAxisField, yAxisField, state.SeriesGroupField, isCritical, block, context);
            List<ChartDataPoint> chartData = points.Points;

            if (isCritical && chartData.Count == 0)
            {
                context.Logger.LogWarning("{block} No data point to plot", block);
                block.ExecutionState.Error =
                    $"The {BlockTypeLabels.Of(block.Type)} block '{block.Name}' found no numbers to plot, so it failed. "
                    + $"Check that its data holds the X-axis field '{xAxisField}' and a number in the Y-axis field '{yAxisField}'.";
                return output;
            }

            List<JObject> axisRows = isCritical ? points.PlottedRows : filteredData;

            // Generate chart configuration using strongly-typed domain entities
            // Non-nullable configuration strings assigned here (for example, title
            // and axis labels) should fall back from nullable ExecutionState values
            // to the corresponding template values at the point of assignment.
            var chartConfig = new ChartConfiguration
            {
                Type = chartType.ToLower(),
                Title = state.ChartTitle ?? props.ChartTitle,
                Data = chartData,
                XAxis = new ChartAxis
                {
                    Field = xAxisField,
                    Label = state.XAxisLabel ?? props.XAxisLabel,
                    Type = DetermineAxisType(axisRows, xAxisField)
                },
                YAxis = new ChartAxis
                {
                    Field = yAxisField,
                    Label = state.YAxisLabel ?? props.YAxisLabel,
                    Type = DetermineAxisType(axisRows, yAxisField)
                },
                Series = GenerateSeriesConfig(chartData, state.SeriesGroupField, state.ColorScheme ?? props.ColorScheme, state.YAxisLabel ?? props.YAxisLabel),
                Layout = new ChartLayout
                {
                    Height = state.ChartHeight ?? props.ChartHeight,
                    Width = state.ChartWidth ?? props.ChartWidth
                },
                Options = new ChartOptions
                {
                    Responsive = true,
                    MaintainAspectRatio = false,
                    ColorScheme = state.ColorScheme ?? props.ColorScheme,
                    ShowLegend = props.ShowLegend,
                    ShowGridLines = props.ShowGridLines,
                    EnableAnimations = props.EnableAnimations,
                    SeriesGroupField = state.SeriesGroupField,
                    Stacking = ParseStackingMode(state.Stacking ?? props.Stacking, chartType)
                },
                Metadata = new ChartMetadata
                {
                    DataPoints = chartData.Count,
                    GeneratedAt = DateTime.UtcNow,
                    Fields = new Dictionary<string, string>
                    {
                        ["x"] = xAxisField,
                        ["y"] = yAxisField,
                        ["series"] = state.SeriesGroupField ?? ""
                    }
                }
            };

            string result = JsonConvert.SerializeObject(chartConfig, Formatting.Indented);

            // Set block response
            _ = new BlockResponse(block)
            {
                RawResponse = result
            };

            context.Logger.LogInformation(
                "{block} Generated dynamic chart with {DataPoints} data points",
                block, chartData.Count);

            context.Logger.LogTrace(
                "{block} Chart configuration:\n{ChartConfig}",
                block, result);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex,
                "{block} Error generating dynamic chart",
                block);

            throw new InvalidOperationException($"Failed to generate dynamic chart: {ex.Message}", ex);
        }

        return output;
    }

    private static List<JObject> ParseDataSource(string dataSourceJson)
    {
        try
        {
            var parsed = JToken.Parse(dataSourceJson);

            // Handle different JSON structures
            return parsed switch
            {
                JArray array => [.. array.Cast<JObject>()],
                JObject obj => [obj],
                _ => throw new InvalidOperationException("Data source must be a JSON object or array")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in data source: {ex.Message}", ex);
        }
    }

    private static List<JObject> ApplyDataFilters(List<JObject> data, string filtersJson)
    {
        if (string.IsNullOrEmpty(filtersJson) || filtersJson == "[]")
        {
            return data;
        }

        try
        {
            List<DataFilter>? filters = JsonConvert.DeserializeObject<List<DataFilter>>(filtersJson);
            if (filters is null || filters.Count == 0)
            {
                return data;
            }

            IEnumerable<JObject> filteredData = data.AsEnumerable();

            foreach (DataFilter filter in filters)
            {
                filteredData = ApplyFilter(filteredData, filter);
            }

            return [.. filteredData];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error applying data filters: {ex.Message}", ex);
        }
    }

    private static IEnumerable<JObject> ApplyFilter(IEnumerable<JObject> data, DataFilter filter)
    {
        return filter.Operation.ToLower() switch
        {
            "equals" => data.Where(item => GetFieldValue(item, filter.Field)?.ToString() == filter.Value),
            "contains" => data.Where(item => GetFieldValue(item, filter.Field)?.ToString()?.Contains(filter.Value) == true),
            "greater" => data.Where(item => CompareNumeric(GetFieldValue(item, filter.Field), filter.Value, (a, b) => a > b)),
            "less" => data.Where(item => CompareNumeric(GetFieldValue(item, filter.Field), filter.Value, (a, b) => a < b)),
            "not_empty" => data.Where(item => !string.IsNullOrEmpty(GetFieldValue(item, filter.Field)?.ToString())),
            _ => data
        };
    }

    private static bool CompareNumeric(JToken? fieldValue, string filterValue, Func<decimal, decimal, bool> comparison)
    {
        return fieldValue is not null && decimal.TryParse(fieldValue.ToString(), out decimal fieldNum) &&
            decimal.TryParse(filterValue, out decimal filterNum) && comparison(fieldNum, filterNum);
    }

    private static JToken? GetFieldValue(JObject item, string fieldPath)
    {
        try
        {
            // Support nested field paths like "data.value" or "metrics[0].amount"
            return item.SelectToken(fieldPath);
        }
        catch
        {
            return null;
        }
    }

    private sealed record ChartPoints(List<ChartDataPoint> Points, List<JObject> PlottedRows);

    private static ChartPoints ProcessDataForChart(
        List<JObject> data,
        string xAxisField,
        string yAxisField,
        string? seriesGroupField,
        bool dropsNonNumericValues,
        Block block,
        BlockExecutionContext context)
    {
        var chartData = new List<ChartDataPoint>();
        List<JObject> plottedRows = [];

        foreach (JObject item in data)
        {
            JToken? xValue = GetFieldValue(item, xAxisField);
            JToken? yValue = GetFieldValue(item, yAxisField);
            string seriesValue = string.IsNullOrEmpty(seriesGroupField) ? "Series 1" :
                              GetFieldValue(item, seriesGroupField)?.ToString() ?? "Unknown";

            if (xValue is not null && yValue is not null)
            {
                bool isNumeric = TryConvertToNumeric(yValue, out decimal yNumber);

                if (!isNumeric && dropsNonNumericValues)
                {
                    context.Logger.LogWarning(
                        "{block} Dropping the point at {X}: its Y value '{Y}' is not a number",
                        block, xValue.ToString(), yValue.ToString());
                    continue;
                }

                chartData.Add(new ChartDataPoint
                {
                    X = xValue.ToString() ?? "",
                    Y = (double)yNumber,
                    Series = seriesValue,
                    RawData = JsonConvert.DeserializeObject<Dictionary<string, object>>(item.ToString())
                });
                plottedRows.Add(item);
            }
        }

        return new ChartPoints(chartData, plottedRows);
    }

    private static bool TryConvertToNumeric(JToken value, out decimal number)
    {
        try
        {
            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    number = value.Value<decimal>();
                    return true;
                case JTokenType.String when decimal.TryParse(value.ToString(), out decimal parsed):
                    number = parsed;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }
        catch
        {
            number = 0;
            return false;
        }
    }

    private static string DetermineAxisType(List<JObject> data, string fieldPath)
    {
        if (data.Count == 0)
        {
            return "category";
        }

        JToken? sampleValue = GetFieldValue(data.First(), fieldPath);
        return sampleValue is null
            ? "category"
            : sampleValue.Type switch
            {
                JTokenType.Integer or JTokenType.Float => "linear",
                JTokenType.Date => "time",
                JTokenType.String when DateTime.TryParse(sampleValue.ToString(), out _) => "time",
                JTokenType.String when decimal.TryParse(sampleValue.ToString(), out _) => "linear",
                _ => "category"
            };
    }

    private static List<ChartSeries> GenerateSeriesConfig(List<ChartDataPoint> chartData, string? seriesGroupField, string colorScheme, string? yAxisLabel = null)
    {
        if (string.IsNullOrEmpty(seriesGroupField))
        {
            // Single series - use yAxis label for a meaningful series name
            string seriesName = !string.IsNullOrEmpty(yAxisLabel) ? yAxisLabel : "Data";

            return
            [
                new() {
                    Name = seriesName,
                    Data = [.. chartData.Select(d => new ChartSeriesDataPoint { X = d.X, Y = d.Y })],
                    Color = GetColorFromScheme(colorScheme, 0)
                }
            ];
        }

        // Multiple series grouped by field
        var seriesGroups = chartData
            .GroupBy(d => d.Series)
            .Select((group, index) => new ChartSeries
            {
                Name = group.Key,
                Data = [.. group.Select(d => new ChartSeriesDataPoint { X = d.X, Y = d.Y })],
                Color = GetColorFromScheme(colorScheme, index)
            })
            .ToList();

        return seriesGroups;
    }

    private static string GetColorFromScheme(string colorScheme, int index)
    {
        var colorSchemes = new Dictionary<string, string[]>
        {
            ["default"] = ["#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#8b5cf6", "#06b6d4"],
            ["professional"] = ["#1f2937", "#374151", "#6b7280", "#9ca3af", "#d1d5db", "#f3f4f6"],
            ["vibrant"] = ["#ff6b6b", "#4ecdc4", "#45b7d1", "#96ceb4", "#feca57", "#ff9ff3"],
            ["earth"] = ["#8b4513", "#a0522d", "#cd853f", "#daa520", "#b8860b", "#d2b48c"]
        };

        string[] colors = colorSchemes.GetValueOrDefault(colorScheme.ToLower(), colorSchemes["default"]);
        return colors[index % colors.Length];
    }

    private static ChartStackingMode? ParseStackingMode(string stackingValue, string chartType)
    {
        // Only apply stacking to bar and column charts
        return chartType is not "bar" and not "column"
            ? null
            : stackingValue?.ToLower() switch
            {
                "none" => ChartStackingMode.None,
                "normal" => ChartStackingMode.Normal,
                "percent" => ChartStackingMode.Percent,
                _ => ChartStackingMode.None // Default to none for bar/column charts
            };
    }
}

// Supporting classes for data processing
public class DataFilter
{
    public string Field { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Value { get; set; } = "";
}
