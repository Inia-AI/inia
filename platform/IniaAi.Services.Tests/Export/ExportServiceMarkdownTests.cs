using IniaAi.Common.Constants;
using System.Text;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.FileFormats;
using IniaAi.FileFormats.ContractTests.Reports;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word;
using IniaAi.Services.Export;
using IniaAi.Services.Export.Models;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Export;

public sealed class ExportServiceMarkdownTests : IDisposable
{
    private const string EXECUTION_ID = "execution-markdown-1";
    private const string ORGANIZATION_ID = "organization-markdown";

    private const string EXPECTED_HEADER = "# Acme Holdings — Credit Review\n\n**" + CounterpartyTerms.SINGULAR + ":** Acme Holdings  \n**Analyst:** Jane Analyst\n\n";

    private const string EXPECTED_MARKDOWN = EXPECTED_HEADER + """
        ## Executive Summary

        #### Overview

        Net leverage is **2.1x**, inside the *3.5x* covenant. See [the method](https://example.com/method).

        - Revenue grew 18%
        - Margin expanded

        ## Financials

        | Metric | FY2024 | FY2025 |
        | --- | --- | --- |
        | Revenue | 264 | 312 |
        | EBITDA | 52.1 | 69.9 |

        Chart: Revenue trend

        | Category | Revenue | EBITDA |
        | --- | --- | --- |
        | FY2024 | 264 | 52.1 |
        | FY2025 | 312 | 69.9 |

        """;

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ExportService _exportService;

    public ExportServiceMarkdownTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _exportService = NewExportService([new MarkdownReportWriter()]);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task ExportAsync_Markdown_ReportWithChartTableCitationAndLeftOutSection_WritesTheMarkdown()
    {
        BinaryContent result = await _exportService.ExportAsync(CreditReviewReportFixture.Build(), ExportFormat.Markdown);

        Assert.Equal(MediaType.TextMarkdown, result.MediaType);
        Assert.Equal(EXPECTED_MARKDOWN, Encoding.UTF8.GetString(result.BinaryData.ToArray()));
    }

    [Fact]
    public async Task ExportAsync_Markdown_UnsafeLinks_BecomePlainTextAndSafeLinksStay()
    {
        BinaryContent result = await _exportService.ExportAsync(CreditReviewReportFixture.WithLinks(), ExportFormat.Markdown);
        string markdown = Encoding.UTF8.GetString(result.BinaryData.ToArray());

        Assert.Contains(CreditReviewReportFixture.LINKS_AS_MARKDOWN, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportByIdAsync_Markdown_StoredReport_ExportsIt()
    {
        WorkflowReport report = CreditReviewReportFixture.Build();
        report.WorkflowExecutionId = EXECUTION_ID;
        _ = _context.WorkflowReports.Add(report);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkflowOutputExportResult result = await _exportService.ExportByIdAsync(EXECUTION_ID, ORGANIZATION_ID, ExportFormat.Markdown);

        Assert.Equal(ExportStatus.Success, result.Status);
        Assert.Equal(EXPECTED_MARKDOWN, Encoding.UTF8.GetString(result.Content!.BinaryData.ToArray()));
    }

    [Fact]
    public async Task ExportByIdAsync_Markdown_UnknownExecution_ReportsNotFound()
    {
        WorkflowOutputExportResult result = await _exportService.ExportByIdAsync("no-such-execution", ORGANIZATION_ID, ExportFormat.Markdown);

        Assert.Equal(ExportStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ExportAsync_WriterRegisteredAfterTheBuildsOwn_ReplacesItForItsFormat()
    {
        using Microsoft.Extensions.DependencyInjection.ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddOpenFileFormats()
            .AddSingleton<IReportFileWriter>(new ReplacementMarkdownWriter())
            .BuildServiceProvider();
        ExportService exportService = NewExportService(services.GetServices<IReportFileWriter>());

        BinaryContent result = await exportService.ExportAsync(CreditReviewReportFixture.Build(), ExportFormat.Markdown);

        Assert.Equal(ReplacementMarkdownWriter.MARK + EXPECTED_MARKDOWN, Encoding.UTF8.GetString(result.BinaryData.ToArray()));
    }

    private ExportService NewExportService(IEnumerable<IReportFileWriter> writers)
    {
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        return new ExportService(
            NullLogger<ExportService>.Instance,
            unitOfWork,
            new UserDisplayNameResolver(unitOfWork),
            writers,
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());
    }

    /// <summary>Another library's Markdown writer, which marks the files it writes.</summary>
    private sealed class ReplacementMarkdownWriter : IReportFileWriter
    {
        public const string MARK = "<!-- written by the replacement -->\n";

        private readonly MarkdownReportWriter _markdown = new();

        public ExportFormat Format => _markdown.Format;

        public string Extension => _markdown.Extension;

        public string ContentType => _markdown.ContentType;

        public async Task<BinaryContent> WriteAsync(IDocumentElement document, ReportFileOptions options)
        {
            BinaryContent file = await _markdown.WriteAsync(document, options);
            return new BinaryContent(BinaryData.FromString(MARK + file.BinaryData), file.MediaType);
        }
    }
}
