using System.IO.Compression;
using System.Text;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Services.Export;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Export;

/// <summary>
/// A report paragraph is model output, and a document the model read can steer it. The Word file of the report
/// must not fetch a picture from another site when the customer, or the customer's own reader, opens it.
/// </summary>
public sealed class ExportTreatsModelTextAsTextTests(ITestOutputHelper output) : IDisposable
{
    private const string REMOTE_IMAGE = "<img src=\"https://example.invalid/p?d=QUOKKA\">";
    private const string REMOTE_LINK = "<a href=\"https://example.invalid/\">the method</a>";
    private const string TEXT = "Net assets are 1,234,567.";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services = new ServiceCollection().AddTestDatabaseServices().BuildServiceProvider();

    [Fact]
    public async Task AWordExport_LinksNoPictureFromAnotherSite_AndKeepsTheText()
    {
        BinaryContent word = await NewExportService().ExportAsync(Report(), ExportFormat.Word);

        using var docx = new ZipArchive(new MemoryStream(word.BinaryData.ToArray()), ZipArchiveMode.Read);
        string relationships = Read(docx, "word/_rels/document.xml.rels");
        string body = Read(docx, "word/document.xml");
        output.WriteLine(relationships);
        Assert.Contains("1,234,567", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            relationships.Split("<Relationship ", StringSplitOptions.RemoveEmptyEntries),
            relationship => relationship.Contains("/image\"", StringComparison.Ordinal) && relationship.Contains("TargetMode=\"External\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMarkdownExport_RecordsWhatItWritesForRawHtml()
    {
        BinaryContent markdown = await NewExportService().ExportAsync(Report(), ExportFormat.Markdown);

        string text = Encoding.UTF8.GetString(markdown.BinaryData.ToArray());
        output.WriteLine(text);
        Assert.Contains("1,234,567", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    private static WorkflowReport Report()
    {
        var report = new WorkflowReport { Title = "Credit report", OrganizationId = "organization-export", WorkflowExecutionId = "execution-export" };
        var section = new ReportSection { Title = "Net assets" };
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = $"<p>{TEXT} See {REMOTE_LINK}.</p>{REMOTE_IMAGE}" });
        report.Add(section);
        return report;
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader((archive.GetEntry(name) ?? throw new InvalidOperationException($"No {name} in the file")).Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private ExportService NewExportService()
    {
        IUserDataUnitOfWork unitOfWork = _services.GetRequiredService<IUserDataUnitOfWork>();
        return new ExportService(
            NullLogger<ExportService>.Instance,
            unitOfWork,
            new UserDisplayNameResolver(unitOfWork),
            [new MarkdownReportWriter(), new OpenXmlWordReportWriter(NullLogger<OpenXmlWordReportWriter>.Instance)],
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());
    }
}
