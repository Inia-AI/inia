using System.IO.Compression;
using System.Text;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Configuration;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Services.Export;
using IniaAi.Services.Export.Models;
using IniaAi.Services.Tests.Documents;
using IniaAi.Services.Tests.Workflows;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Export;

/// <summary>
/// The Word and Markdown files of a report from <see cref="MarkerWorkflow"/> hold the report's text and none of the
/// workflow's instructions, in the file's bytes or in any part of a Word file.
/// </summary>
public sealed class ExportCarriesNoInstructionsTests(DocumentsFixture fixture, ITestOutputHelper output) : IClassFixture<DocumentsFixture>
{
    private const string ANSWER = "Net assets are 1,234,567.";

    [Theory]
    [InlineData(ExportFormat.Word)]
    [InlineData(ExportFormat.Markdown)]
    public async Task AReportsExport_HoldsItsTextAndNoInstructionOfItsWorkflow(ExportFormat format)
    {
        using var run = new MarkerRun(fixture, output, ScriptedChatConnector.Answering($"<answer>{ANSWER}</answer>"));
        _ = await run.RunAsync(TestContext.Current.CancellationToken);

        IUserDataUnitOfWork unitOfWork = run.UnitOfWork;
        var exportService = new ExportService(
            NullLogger<ExportService>.Instance,
            unitOfWork,
            new UserDisplayNameResolver(unitOfWork),
            [new MarkdownReportWriter(), new OpenXmlWordReportWriter(NullLogger<OpenXmlWordReportWriter>.Instance)],
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());
        WorkflowOutputExportResult export = await exportService.ExportByIdAsync(run.ExecutionId!, MarkerRun.ORGANIZATION_ID, format);

        Assert.Equal(ExportStatus.Success, export.Status);
        string text = Text(export.Content!.BinaryData.ToArray());
        Assert.Contains(ANSWER, text, StringComparison.Ordinal);
        string[] carried = [.. MarkerWorkflow.Instructions.Where(marker => text.Contains(marker, StringComparison.Ordinal))];
        Assert.True(carried.Length == 0, $"The {format} file carries {string.Join(", ", carried)}");
    }

    /// <summary>The file as text, with every entry of a zip file read as text after it.</summary>
    private static string Text(byte[] bytes)
    {
        var text = new StringBuilder(Encoding.UTF8.GetString(bytes));
        if (bytes is [(byte)'P', (byte)'K', ..])
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                _ = text.Append('\n').Append(entry.FullName).Append('\n').Append(reader.ReadToEnd());
            }
        }

        return text.ToString();
    }
}
