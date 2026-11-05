using ErrorOr;
using IniaAi.Application.Queries.WorkflowReports.ListReportSections;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Enums.Charts;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using System.Text.Json;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class WorkflowReportReadTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-report-read";
    private const string EXECUTION_ID = "execution-report-read";

    [Fact]
    public async Task TheReportSections_ReadAsTheSameJson()
    {
        var report = new WorkflowReport
        {
            Id = "report-read",
            WorkflowExecutionId = EXECUTION_ID,
            OrganizationId = ORGANIZATION_ID,
            Title = "Annual review",
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        var appendix = new ReportSection { Id = "section-appendix", Title = "Appendix", ExcludeFromWordExport = true, CreatedAt = Created, UpdatedAt = Created };
        var summary = new ReportSection
        {
            Id = "section-summary",
            Title = "Summary",
            Layout = new GridLayoutOutput
            {
                Name = "Summary grid",
                Mode = "grid",
                Columns = 3,
                Elements =
                [
                    new GridElementPosition { BlockOrder = 1, Row = 1, Column = 1, RowSpan = 2, ColumnSpan = 1 },
                    new GridElementPosition { BlockOrder = 2, Row = 1, Column = 2, RowSpan = 1, ColumnSpan = 2 },
                ],
            },
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        report.Add(summary);
        report.Add(appendix);

        summary.Paragraphs.Add(new ReportParagraph
        {
            Id = "paragraph-table",
            Kind = ReportParagraphKind.Table,
            Order = 2,
            Title = "Key figures",
            Subtitle = "In millions",
            ReplacementTag = "key_figures",
            BlockId = "block-table",
            TablePayload = new TableOutput
            {
                TableName = "Key figures",
                TableConfig = [new TableConfig { Header = "Year", Format = "text", GroupName = "Period" }],
                TableData = [["Year", "Revenue"], ["2025", "4.2"]],
            },
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        summary.Paragraphs.Add(new ReportParagraph
        {
            Id = "paragraph-text",
            Kind = ReportParagraphKind.Paragraph,
            Order = 1,
            Title = "Overview",
            Confidence = 0.82,
            Content = "Revenue rose 12 percent.",
            ReplacementTag = "overview",
            BlockId = "block-text",
            Sources = ["Annual report", "Website"],
            Citations =
            [
                new Citation
                {
                    Id = "citation-1",
                    IsNumerical = true,
                    Sources =
                    [
                        new DocumentSource { DocumentId = "document-1", PageId = "page-4", PageNumber = 4, FilePageNumber = 6, DocumentName = "Annual report", Unverified = true, CitedText = "Revenue 4.2m" },
                        new UrlSource { Url = "https://example.com/results", CitedText = "Results" },
                        new BlockSource
                        {
                            BlockId = "block-upstream",
                            BlockTitle = "Upstream",
                            CitedText = "From upstream",
                            UpstreamSources = [new DocumentSource { DocumentId = "document-2", PageId = "page-1", PageNumber = 1, FilePageNumber = 1, CitedText = "Upstream text" }],
                            MatchingSources = [new UrlSource { Url = "https://example.com/match", CitedText = "Match" }],
                        },
                    ],
                },
            ],
            CitedSourcesByBlock =
            [
                new BlockCitedSources { BlockId = "block-upstream", Sources = [new UrlSource { Url = "https://example.com/cited", CitedText = "Cited" }] },
            ],
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        appendix.Paragraphs.Add(new ReportParagraph
        {
            Id = "paragraph-chart",
            Kind = ReportParagraphKind.DynamicChart,
            Order = 1,
            Title = "Revenue trend",
            BlockId = "block-chart",
            ChartPayload = new ChartOutput
            {
                Name = "Revenue",
                ChartData = [new ChartDataPoint { X = "2024", Y = 3.7, Series = "Revenue", RawData = new Dictionary<string, object> { ["note"] = "restated" } }],
                ChartConfig = new ChartConfiguration
                {
                    Type = "bar",
                    Title = "Revenue by year",
                    Data = [new ChartDataPoint { X = "2025", Y = 4.2, Series = "Revenue" }],
                    XAxis = new ChartAxis { Type = "category", Field = "year", Label = "Year" },
                    YAxis = new ChartAxis { Type = "linear", Field = "revenue", Label = "Revenue" },
                    Series = [new ChartSeries { Name = "Revenue", Color = "#336699", Data = [new ChartSeriesDataPoint { X = "2025", Y = 4.2 }] }],
                    Options = new ChartOptions { ShowLegend = false, ColorScheme = "muted", EnableExport = true, SeriesGroupField = "segment", Stacking = ChartStackingMode.Percent },
                    Layout = new ChartLayout { Width = "50%", Height = "300" },
                    Metadata = new ChartMetadata { Fields = new Dictionary<string, string> { ["year"] = "Year" }, DataPoints = 2, GeneratedAt = Created, Currency = "USD", Unit = "m" },
                },
            },
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        appendix.Paragraphs.Add(new ReportParagraph { Id = "paragraph-divider", Kind = ReportParagraphKind.Divider, Order = 0, CreatedAt = Created, UpdatedAt = Created });

        _ = UserData.WorkflowReports.Add(report);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<List<ReportSectionDto>> result = await Handler<ListReportSectionsQueryHandler>().Handle(
            new ListReportSectionsQuery(EXECUTION_ID, ORGANIZATION_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        AssertMatchesFixture(result.Value, "ReportSections");
    }

    [Fact]
    public async Task AReportWhosePartsWereStoredAsNull_ReadsThemAsEmpty()
    {
        ChartConfiguration pie = JsonSerializer.Deserialize<ChartConfiguration>(
            """{"type":"pie","title":"Revenue mix","data":null,"xAxis":null,"yAxis":null,"series":null,"options":null,"layout":null,"metadata":null}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var report = new WorkflowReport { Id = "report-nulls", WorkflowExecutionId = EXECUTION_ID, OrganizationId = ORGANIZATION_ID, CreatedAt = Created, UpdatedAt = Created };
        var section = new ReportSection { Id = "section-nulls", Layout = new GridLayoutOutput { Elements = null! }, CreatedAt = Created, UpdatedAt = Created };
        report.Add(section);
        section.Paragraphs.Add(new ReportParagraph
        {
            Id = "paragraph-pie",
            Kind = ReportParagraphKind.DynamicChart,
            ChartPayload = new ChartOutput { Name = pie.Title, ChartData = pie.Data, ChartConfig = pie },
            Citations = [new Citation { Id = "citation-nulls", Sources = null! }],
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        section.Paragraphs.Add(new ReportParagraph
        {
            Id = "paragraph-table",
            Kind = ReportParagraphKind.Table,
            Order = 1,
            TablePayload = new TableOutput { TableName = "Empty", TableConfig = null!, TableData = null! },
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        _ = UserData.WorkflowReports.Add(report);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<List<ReportSectionDto>> result = await Handler<ListReportSectionsQueryHandler>().Handle(
            new ListReportSectionsQuery(EXECUTION_ID, ORGANIZATION_ID),
            TestContext.Current.CancellationToken);

        ReportSectionDto read = Assert.Single(result.Value);
        Assert.Empty(read.Layout!.Elements);
        ChartOutputDto chart = read.Paragraphs[0].ChartPayload!;
        Assert.Empty(chart.ChartData);
        Assert.Empty(chart.ChartConfig.Data);
        Assert.Empty(chart.ChartConfig.Series);
        Assert.Empty(chart.ChartConfig.Metadata.Fields);
        Assert.Equal(new ChartAxis().Type, chart.ChartConfig.XAxis.Type);
        Assert.Empty(Assert.Single(read.Paragraphs[0].Citations).Sources);
        Assert.Empty(read.Paragraphs[1].TablePayload!.TableConfig);
        Assert.Empty(read.Paragraphs[1].TablePayload!.TableData);
    }
}
