using IniaAi.Common.Content;
using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.ContractTests.Reports;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Services.Export;
using IniaAi.Testing.Ooxml;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace IniaAi.Services.Tests.Export;

public class ChartImageRenderServiceTests
{
    [RequiresChromeTheory]
    [InlineData("line")]
    [InlineData("area")]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("pie")]
    [InlineData("donut")]
    [InlineData("scatter")]
    [InlineData("bubble")]
    public async Task EachStoredChartType_DrawsAPngOfTheRequestedSize_InSeveralColours(string chartType)
    {
        byte[] image = await NewService().RenderChartToImageAsync(ChartOf(chartType), 600, 400, cancellationToken: TestContext.Current.CancellationToken);

        using SKBitmap bitmap = SKBitmap.Decode(image);
        Assert.Equal(600, bitmap.Width);
        Assert.Equal(400, bitmap.Height);
        Assert.True(bitmap.Pixels.Distinct().Count() > 2, "The chart image has no more than two colours.");
    }

    [RequiresChromeFact]
    public async Task AChartThePageCannotDraw_FailsInsteadOfGivingAnEmptyPicture()
    {
        var empty = new ChartConfiguration { Type = "line", Title = "Nothing to draw" };

        InvalidOperationException refusal = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            NewService().RenderChartToImageAsync(empty, 600, 400, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("The chart has no data to draw.", refusal.Message, StringComparison.Ordinal);
    }

    [RequiresChromeFact]
    public async Task OneSession_DrawsChartAfterChart_AndKeepsDrawingAfterAChartThePageRefuses()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using IChartImageSession session = NewService().Open();

        byte[] first = await session.RenderChartToImageAsync(ChartOf("line"), 600, 400, cancellationToken: cancellationToken);
        _ = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            session.RenderChartToImageAsync(new ChartConfiguration { Type = "line", Title = "Nothing to draw" }, 600, 400, cancellationToken: cancellationToken));
        byte[] third = await session.RenderChartToImageAsync(ChartOf("pie"), 400, 400, cancellationToken: cancellationToken);

        using SKBitmap line = SKBitmap.Decode(first);
        using SKBitmap pie = SKBitmap.Decode(third);
        Assert.Equal(600, line.Width);
        Assert.Equal(400, pie.Width);
    }

    [RequiresChromeFact]
    public async Task TheWordExport_PutsInEachChartAsAPicture_AndAChartThePageCannotDrawAsItsData()
    {
        var writer = new OpenXmlWordReportWriter(NullLogger<OpenXmlWordReportWriter>.Instance);
        await using IChartImageSession session = NewService().Open();
        var options = new ReportFileOptions(session);
        WordFile withoutCharts = await WriteAsync(writer, SmallReportFixtures.OneParagraph("No charts."), options);

        var report = new WorkflowReport { Title = "Charts" };
        var section = new ReportSection { Title = "Market Data" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.DynamicChart, Title = "Share price", ChartPayload = NewsMarketDataFixture.StockPriceChart() });
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.DynamicChart, Title = "Revenue", ChartPayload = new ChartOutput { ChartConfig = ChartOf("column") } });
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.DynamicChart, Title = "Empty", ChartPayload = new ChartOutput { ChartConfig = new ChartConfiguration { Type = "line", Title = "Empty" } } });
        WordFile file = await WriteAsync(writer, report, options);

        Assert.Equal(withoutCharts.Drawings + 2, file.Drawings);
        Assert.Equal(withoutCharts.Tables, file.Tables);
        Assert.Contains(ChartDataTable.NO_DATA_TEXT, file.Text, StringComparison.Ordinal);
    }

    private static async Task<WordFile> WriteAsync(IReportFileWriter writer, WorkflowReport report, ReportFileOptions options)
    {
        BinaryContent file = await writer.WriteAsync(WorkflowDocumentAdapter.ToDocumentElement(report), options);
        return new WordFile(file.BinaryData.ToArray());
    }

    private static ChartImageRenderService NewService()
    {
        return new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance);
    }

    private static ChartConfiguration ChartOf(string chartType)
    {
        bool categories = chartType is "line" or "area" or "bar" or "column";
        bool slices = chartType is "pie" or "donut";
        return new ChartConfiguration
        {
            Type = chartType,
            Title = $"A {chartType} chart",
            XAxis = new ChartAxis { Field = "x", Label = "Year" },
            YAxis = new ChartAxis { Field = "y", Label = "Revenue" },
            Series = categories
                ?
                [
                    new ChartSeries { Name = "Revenue", Data = [new ChartSeriesDataPoint { X = "2024", Y = 3.7 }, new ChartSeriesDataPoint { X = "2025", Y = 4.2 }] },
                    new ChartSeries { Name = "Costs", Data = [new ChartSeriesDataPoint { X = "2024", Y = 1.2 }, new ChartSeriesDataPoint { X = "2025", Y = 1.9 }] },
                ]
                : [],
            Data = categories
                ? []
                : slices
                    ? [new ChartDataPoint { X = "Equity", Y = 30 }, new ChartDataPoint { X = "Debt", Y = 70 }]
                    : [new ChartDataPoint { X = "1", Y = 2, Series = "North" }, new ChartDataPoint { X = "-3", Y = 4, Series = "South" }],
        };
    }
}
