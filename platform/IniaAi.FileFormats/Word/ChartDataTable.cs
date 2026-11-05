using System.Globalization;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.FileFormats.Word;

/// <summary>
/// A chart's data as text: one row per category, one column per series. An export writes it
/// where it cannot draw the chart.
/// </summary>
public sealed class ChartDataTable
{
    public const string NO_DATA_TEXT = "No data available";

    private const string CATEGORY_HEADER = "Category";
    private const string UNNAMED_SERIES_HEADER = "Value";
    private const string CAPTION_PREFIX = "Chart: ";

    private ChartDataTable(string caption, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Caption = caption;
        Header = header;
        Rows = rows;
    }

    public string Caption { get; }

    public IReadOnlyList<string> Header { get; }

    /// <summary>The category, then each series' value; empty where the series has no point in that category.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public static ChartDataTable From(ChartOutput chart)
    {
        List<ChartDataPoint> points = chart.ChartData;
        List<string> series = [.. points.Select(point => point.Series).Distinct()];
        List<string> categories = [.. points.Select(point => point.X).Distinct()];
        var values = new Dictionary<(string Category, string Series), double>(points.Count);
        foreach (ChartDataPoint point in points)
        {
            _ = values.TryAdd((point.X, point.Series), point.Y);
        }

        List<string> header = [CATEGORY_HEADER, .. series.Select(name => string.IsNullOrEmpty(name) ? UNNAMED_SERIES_HEADER : name)];
        List<IReadOnlyList<string>> rows = [.. categories.Select(category => (IReadOnlyList<string>)
            [category, .. series.Select(name => values.TryGetValue((category, name), out double value) ? value.ToString(CultureInfo.InvariantCulture) : string.Empty)])];

        return new ChartDataTable($"{CAPTION_PREFIX}{chart.Name}", header, rows);
    }
}
