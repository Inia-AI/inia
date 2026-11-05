using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.FileFormats.Word;

namespace IniaAi.FileFormats.Tests.Word;

public class ChartDataTableTests
{
    [Fact]
    public void From_MultiSeriesChart_NamesEachSeriesColumnAndRowsEachCategory()
    {
        var chart = new ChartOutput
        {
            Name = "Liquidity",
            ChartData =
            [
                new ChartDataPoint { X = "2023", Y = 1.6, Series = "Current Ratio" },
                new ChartDataPoint { X = "2024", Y = 1.85, Series = "Current Ratio" },
                new ChartDataPoint { X = "2023", Y = 0.0425, Series = "Cash Ratio" },
                new ChartDataPoint { X = "2024", Y = 2, Series = "Cash Ratio" },
            ],
        };

        ChartDataTable table = ChartDataTable.From(chart);

        Assert.Equal("Chart: Liquidity", table.Caption);
        Assert.Equal(["Category", "Current Ratio", "Cash Ratio"], table.Header);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["2023", "1.6", "0.0425"], table.Rows[0]);
        Assert.Equal(["2024", "1.85", "2"], table.Rows[1]);
    }

    [Fact]
    public void From_SeriesWithoutAName_TitlesItsColumnValue()
    {
        var chart = new ChartOutput
        {
            Name = "Revenue",
            ChartData = [new ChartDataPoint { X = "FY24", Y = 312 }],
        };

        ChartDataTable table = ChartDataTable.From(chart);

        Assert.Equal(["Category", "Value"], table.Header);
        Assert.Equal(["FY24", "312"], Assert.Single(table.Rows));
    }

    [Fact]
    public void From_SeriesWithoutAPointInACategory_LeavesThatCellEmpty()
    {
        var chart = new ChartOutput
        {
            Name = "Scatter",
            ChartData =
            [
                new ChartDataPoint { X = "A", Y = 1, Series = "First" },
                new ChartDataPoint { X = "B", Y = 2, Series = "Second" },
            ],
        };

        ChartDataTable table = ChartDataTable.From(chart);

        Assert.Equal(["A", "1", ""], table.Rows[0]);
        Assert.Equal(["B", "", "2"], table.Rows[1]);
    }

    [Fact]
    public void From_ChartWithoutData_HasNoRows()
    {
        ChartDataTable table = ChartDataTable.From(new ChartOutput { Name = "Empty" });

        Assert.Equal("Chart: Empty", table.Caption);
        Assert.Empty(table.Rows);
    }
}
