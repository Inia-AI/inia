using ErrorOr;
using IniaAi.Application.Queries.WorkflowReports.ListReportSections;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto.Citations;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.WorkflowReports;

public class ListReportSectionsQueryHandlerTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ListReportSectionsQueryHandler _handler;

    public ListReportSectionsQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new ListReportSectionsQueryHandler(unitOfWork);
    }

    [Fact]
    public async Task Handle_ReturnsSectionsOrderedByOrder()
    {
        await SeedReportAsync("exec-order", "org-order");

        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("exec-order", "org-order"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Section Zero", result.Value[0].Title);
        Assert.Equal("Section One", result.Value[1].Title);
        Assert.False(result.Value[0].ExcludeFromWordExport);
        Assert.True(result.Value[1].ExcludeFromWordExport);
    }

    [Fact]
    public async Task Handle_ReturnsParagraphsOrderedWithKindsAndPayloads()
    {
        await SeedReportAsync("exec-kinds", "org-kinds");

        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("exec-kinds", "org-kinds"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportSectionDto sectionZero = result.Value[0];

        Assert.NotNull(sectionZero.Layout);
        Assert.Equal("grid", sectionZero.Layout!.Mode);

        Assert.Equal(2, sectionZero.Paragraphs.Count);

        ReportParagraphDto chart = sectionZero.Paragraphs[0];
        Assert.Equal(ReportParagraphKindDto.DynamicChart, chart.Kind);
        Assert.NotNull(chart.ChartPayload);
        Assert.Equal("Revenue chart", chart.ChartPayload!.Name);

        ReportParagraphDto table = sectionZero.Paragraphs[1];
        Assert.Equal(ReportParagraphKindDto.Table, table.Kind);
        Assert.NotNull(table.TablePayload);
        Assert.Equal("Revenue table", table.TablePayload!.TableName);
        Assert.Single(table.TablePayload.TableData);
    }

    [Fact]
    public async Task Handle_BlockCitation_CarriesItsMatchingSourcesAndTheBlockListAsync()
    {
        await SeedBlockCitationReportAsync("exec-block", "org-block");

        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("exec-block", "org-block"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportParagraphDto paragraph = Assert.Single(Assert.Single(result.Value).Paragraphs);

        CitationDto citation = Assert.Single(paragraph.Citations);
        BlockCitationSourceDto blockSource = Assert.IsType<BlockCitationSourceDto>(Assert.Single(citation.Sources));
        UrlCitationSourceDto match = Assert.IsType<UrlCitationSourceDto>(Assert.Single(blockSource.MatchingSources));
        Assert.Equal("https://example.org/people/alex-rivera", match.Url);

        BlockCitedSourcesDto list = Assert.Single(paragraph.CitedSourcesByBlock);
        Assert.Equal("block-synthesis", list.BlockId);
        Assert.Collection(
            list.Sources,
            source => Assert.Equal(12, Assert.IsType<DocumentCitationSourceDto>(source).PageNumber),
            source => Assert.Equal("https://example.org/people/alex-rivera", Assert.IsType<UrlCitationSourceDto>(source).Url));
    }

    [Fact]
    public async Task Handle_OtherOrganization_ReturnsEmpty()
    {
        await SeedReportAsync("exec-scope", "org-owner");

        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("exec-scope", "org-intruder"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_AParagraphWhoseChartTheReportLeftOut_ReadsAtTheFullWidth()
    {
        var report = new WorkflowReport { WorkflowExecutionId = "exec-no-chart", OrganizationId = "org-no-chart", Title = "Report" };
        var section = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Title = "Liquidity",
            Layout = new GridLayoutOutput
            {
                Mode = "grid",
                Columns = 5,
                Elements =
                [
                    new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, RowSpan = 1, ColumnSpan = 3 },
                    new GridElementPosition { BlockOrder = 1, Row = 1, Column = 4, RowSpan = 1, ColumnSpan = 2 },
                ],
            },
        };
        section.Paragraphs.Add(new ReportParagraph { SectionId = section.Id, Section = section, Kind = ReportParagraphKind.Paragraph, Order = 0, Content = "Liquidity is strong." });
        report.Sections.Add(section);
        _ = await _context.Set<WorkflowReport>().AddAsync(report, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("exec-no-chart", "org-no-chart"),
            TestContext.Current.CancellationToken);

        GridElementPositionDto paragraph = Assert.Single(Assert.Single(result.Value).Layout!.Elements);
        Assert.Equal((0, 1, 5), (paragraph.BlockOrder, paragraph.Column, paragraph.ColumnSpan));
    }

    [Fact]
    public async Task Handle_UnknownExecution_ReturnsEmpty()
    {
        ErrorOr<List<ReportSectionDto>> result = await _handler.Handle(
            new ListReportSectionsQuery("nope", "org-x"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    private async Task SeedReportAsync(string executionId, string organizationId)
    {
        var report = new WorkflowReport
        {
            WorkflowExecutionId = executionId,
            OrganizationId = organizationId,
            Title = "Report",
        };

        var sectionZero = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Order = 0,
            Title = "Section Zero",
            Layout = new GridLayoutOutput { Mode = "grid", Columns = 2 },
        };
        var sectionOne = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Order = 1,
            Title = "Section One",
            ExcludeFromWordExport = true,
        };

        var chart = new ReportParagraph
        {
            SectionId = sectionZero.Id,
            Section = sectionZero,
            Kind = ReportParagraphKind.DynamicChart,
            Order = 0,
            ChartPayload = new ChartOutput { Name = "Revenue chart" },
        };
        var table = new ReportParagraph
        {
            SectionId = sectionZero.Id,
            Section = sectionZero,
            Kind = ReportParagraphKind.Table,
            Order = 1,
            TablePayload = new TableOutput { TableName = "Revenue table", TableData = [["2025", "120"]] },
        };
        sectionZero.Paragraphs.Add(chart);
        sectionZero.Paragraphs.Add(table);
        report.Sections.Add(sectionOne);
        report.Sections.Add(sectionZero);

        _ = await _context.Set<WorkflowReport>().AddAsync(report, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task SeedBlockCitationReportAsync(string executionId, string organizationId)
    {
        var report = new WorkflowReport
        {
            WorkflowExecutionId = executionId,
            OrganizationId = organizationId,
            Title = "Report",
        };
        var section = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Order = 0,
            Title = "Executive Summary",
        };
        var profile = new UrlSource { Url = "https://example.org/people/alex-rivera", CitedText = "start date: 2011-05-01" };
        var paragraph = new ReportParagraph
        {
            SectionId = section.Id,
            Section = section,
            Kind = ReportParagraphKind.Paragraph,
            Content = "Partner since <fact id=\"c0\">May 2011</fact>.",
            Citations =
            [
                new Citation
                {
                    Id = "c0",
                    IsNumerical = true,
                    Sources =
                    [
                        new BlockSource
                        {
                            BlockId = "block-synthesis",
                            BlockTitle = "Key Principals",
                            CitedText = "a paraphrase",
                            MatchingSources = [profile],
                        },
                    ],
                },
            ],
            CitedSourcesByBlock =
            [
                new BlockCitedSources
                {
                    BlockId = "block-synthesis",
                    Sources =
                    [
                        new DocumentSource { DocumentId = "doc-1", PageId = "page-12", PageNumber = 12, DocumentName = "Fund.pdf", CitedText = "Net assets" },
                        profile,
                    ],
                },
            ],
        };
        section.Paragraphs.Add(paragraph);
        report.Sections.Add(section);

        _ = await _context.Set<WorkflowReport>().AddAsync(report, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
}
