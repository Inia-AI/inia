using System.Text.Json;
using System.Text.Json.Serialization;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

/// <summary>
/// Invented "News &amp; Market Data" section text in the shape a workflow produces: a long
/// left-column news block that a grid layout once confined to a narrow column for its entire
/// length, a market data block and a chart payload. It describes no real company or person.
/// </summary>
public static class NewsMarketDataFixture
{
    public const string CHART_TITLE = "Stock Price Change Chart";

    public const string NewsAndIssuesMarkdown = """
- [2023-02 to 2025-11]: Harrowgate Aerostructures plc's <fact id="c7">2023</fact> Consent Order with the national aviation regulator, under which the company agreed to pay <fact id="c10">£4.2 million</fact> and to rebuild its quality management system after inspectors found gaps in part traceability, was extended in May <fact id="c0">2025</fact> when the regulator reported that two of the eleven corrective actions were still open. The company appointed an independent monitor, published a remediation plan with quarterly milestones, and told lenders that the extension does not affect any customer approval or delivery schedule.

- [2024-03 to 2025-09]: Following a <fact id="c4">March 2024</fact> quality escape in which a batch of wing ribs left the Thornby plant with incomplete heat-treatment records, the company's largest customer capped weekly deliveries from that plant at <fact id="c1">40</fact> shipsets and placed a resident inspector on site. On <fact id="c19">12 September 2025</fact>, the cap was lifted after six months without a recorded escape, and the company reported that the remediation cost <fact id="c2">£6.8 million</fact> in total, most of it booked in the <fact id="c3">2024</fact> financial year.

- [2024-06 to 2025-04]: In June <fact id="c0">2024</fact>, a former supplier started a contract claim against the company for <fact id="c6">£12.5 million</fact>, alleging that the company ended a forging agreement without the required notice. In April <fact id="c0">2025</fact>, the parties settled for <fact id="c11">£3.1 million</fact> without admission of liability, and the settlement was fully covered by an existing provision, so it had no further effect on reported earnings or on covenant headroom.

- [2025-09]: The company opened its new machining plant in September <fact id="c18">2025</fact> at a total cost of <fact id="c14">£118 million</fact>, financed by a term loan of <fact id="c15">£80 million</fact> and internal cash. The plant adds about <fact id="c16">30%</fact> to wing-rib capacity, employs about 420 people, and is expected to reach full output during the <fact id="c18">2027</fact> financial year once the second and third machining cells have been qualified by both major customers.

- [2025-04 to 2025-12]: On 22 April <fact id="c18">2025</fact>, the company agreed to sell its non-core tooling business to a private buyer for <fact id="c17">£36 million</fact> in cash. The sale closed by the end of <fact id="c18">2025</fact> and produced a gain of £9.4 million. Management described the sale as a way to focus on core structures work, reduce net debt and protect the company's credit standing while the new plant ramps up.
""";

    public const string MarketDataMarkdown = """
- Company: Harrowgate Aerostructures plc (HRWG), London Stock Exchange
- Sector / Industry: Industrials / Aerospace & Defense
- Stock Price: 412.50 GBp
- Market Capitalisation: 1,286,400,000 GBP (~1.29 billion)
- 52-Week Range: 356.00 – 498.20 (currently at 39.73% of range)
- Beta: 1.142
- Price Change — 1M: -6.12% | 3M: -3.85% | 6M: 2.94% | YTD: -1.08% | 1Y: -2.31%

Harrowgate Aerostructures plc's shares are trading at 412.50 GBp, in the lower half (39.73%) of the 52-week range of 356.00–498.20, and negative momentum across the 1-month (-6.12%), 3-month (-3.85%), YTD (-1.08%) and 1-year (-2.31%) timeframes points to continued near-term pressure. With a beta of 1.142, the stock is moderately more volatile than the broader market, and the lower dividend and the multi-year decline (5Y: -11.40%) suggest the company is still working through margin pressure, even as its 6-month gain of 2.94% shows some recovery.
""";

    public const string StockPriceChartPayloadJson = """
{"name": "Stock Price Change (%)", "chartData": [{"x": "1M", "y": -6.12, "series": "Series 1", "rawData": {"value": -6.12, "period": "1M"}}, {"x": "3M", "y": -3.85, "series": "Series 1", "rawData": {"value": -3.85, "period": "3M"}}, {"x": "6M", "y": 2.94, "series": "Series 1", "rawData": {"value": 2.94, "period": "6M"}}, {"x": "YTD", "y": -1.08, "series": "Series 1", "rawData": {"value": -1.08, "period": "YTD"}}, {"x": "1Y", "y": -2.31, "series": "Series 1", "rawData": {"value": -2.31, "period": "1Y"}}, {"x": "3Y", "y": -4.65, "series": "Series 1", "rawData": {"value": -4.65, "period": "3Y"}}, {"x": "5Y", "y": -11.40, "series": "Series 1", "rawData": {"value": -11.40, "period": "5Y"}}], "outputType": "Chart", "chartConfig": {"data": [{"x": "1M", "y": -6.12, "series": "Series 1", "rawData": {"value": -6.12, "period": "1M"}}, {"x": "3M", "y": -3.85, "series": "Series 1", "rawData": {"value": -3.85, "period": "3M"}}, {"x": "6M", "y": 2.94, "series": "Series 1", "rawData": {"value": 2.94, "period": "6M"}}, {"x": "YTD", "y": -1.08, "series": "Series 1", "rawData": {"value": -1.08, "period": "YTD"}}, {"x": "1Y", "y": -2.31, "series": "Series 1", "rawData": {"value": -2.31, "period": "1Y"}}, {"x": "3Y", "y": -4.65, "series": "Series 1", "rawData": {"value": -4.65, "period": "3Y"}}, {"x": "5Y", "y": -11.40, "series": "Series 1", "rawData": {"value": -11.40, "period": "5Y"}}], "type": "column", "title": "Stock Price Change (%)", "xAxis": {"type": "category", "field": "period", "label": "Period"}, "yAxis": {"type": "linear", "field": "value", "label": "% Return"}, "layout": {"width": "100%", "height": "400"}, "series": [{"data": [{"x": "1M", "y": -6.12}, {"x": "3M", "y": -3.85}, {"x": "6M", "y": 2.94}, {"x": "YTD", "y": -1.08}, {"x": "1Y", "y": -2.31}, {"x": "3Y", "y": -4.65}, {"x": "5Y", "y": -11.40}], "name": "% Return", "color": "#3b82f6"}], "options": {"stacking": "none", "responsive": true, "showLegend": false, "colorScheme": "default", "enableExport": null, "showGridLines": true, "enableAnimations": true, "seriesGroupField": "", "maintainAspectRatio": false}, "metadata": {"unit": null, "fields": {"x": "period", "y": "value", "series": ""}, "currency": null, "dataPoints": 7, "generatedAt": "2026-01-15T09:30:00.0000000Z"}}}
""";

    private static readonly JsonSerializerOptions _chartJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>A grid section: the news block spans the second row, and the market data and the chart share the first.</summary>
    public static WorkflowReport Build()
    {
        var report = new WorkflowReport
        {
            Title = "Harrowgate Aerostructures — News & Market Data",
            AnalystName = CreditReviewReportFixture.ANALYST,
            ReviewDate = new DateTime(2026, 6, 17),
        };

        var section = new ReportSection
        {
            Title = "News & Market Data",
            Layout = new GridLayoutOutput
            {
                Mode = GridLayoutOutput.GRID_MODE,
                Columns = 5,
                Elements =
                [
                    new GridElementPosition { BlockOrder = 0, Row = 2, Column = 1, ColumnSpan = 5, RowSpan = 1 },
                    new GridElementPosition { BlockOrder = 1, Row = 1, Column = 1, ColumnSpan = 3, RowSpan = 1 },
                    new GridElementPosition { BlockOrder = 2, Row = 1, Column = 4, ColumnSpan = 2, RowSpan = 1 },
                ],
            },
        };
        report.Add(section);

        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "News & Issues", Subtitle = "News & Issues", Content = NewsAndIssuesMarkdown });
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "Market Data", Subtitle = "Market Data", Content = MarketDataMarkdown });
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.DynamicChart,
            Title = CHART_TITLE,
            ChartPayload = StockPriceChart(),
        });

        return report;
    }

    /// <summary>One section that holds only the stock price chart.</summary>
    public static WorkflowReport ChartOnly()
    {
        var report = new WorkflowReport { Title = "Harrowgate Aerostructures — Share Price" };
        var section = new ReportSection { Title = "Market Data" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.DynamicChart, Title = CHART_TITLE, ChartPayload = StockPriceChart() });
        return report;
    }

    public static ChartOutput StockPriceChart()
    {
        return JsonSerializer.Deserialize<ChartOutput>(StockPriceChartPayloadJson, _chartJson)!;
    }
}
