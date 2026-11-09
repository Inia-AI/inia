using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Unlock;
using IniaAi.FileFormats.Word;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Testing.Documents;

/// <summary>
/// The file-format implementation that tests read and write documents with, taken from the same
/// registration the hosts use.
/// </summary>
public static class FileFormatsUnderTest
{
    private static readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddFileFormatsUnderTest()
        .BuildServiceProvider();

    public static IPdfFormat Pdf { get; } = _services.GetRequiredService<IPdfFormat>();

    public static IWorkbookFormat Workbooks { get; } = _services.GetRequiredService<IWorkbookFormat>();

    public static IEditableWorkbookFormat EditableWorkbooks { get; } = _services.GetRequiredService<IEditableWorkbookFormat>();

    public static IWordTemplateFormat WordTemplates { get; } = _services.GetRequiredService<IWordTemplateFormat>();

    public static IWorkbookToWordConverter WorkbookToWord { get; } = _services.GetRequiredService<IWorkbookToWordConverter>();

    public static IDocumentUnlocker Unlocker { get; } = _services.GetRequiredService<IDocumentUnlocker>();

    public static IReadableFormats ReadableFormats { get; } = _services.GetRequiredService<IReadableFormats>();

    public static IWordPageReader WordPages { get; } = _services.GetRequiredService<IWordPageReader>();

    public static IDocumentPageCounter PageCounter { get; } = _services.GetRequiredService<IDocumentPageCounter>();

    public static IReadOnlyList<IReportFileWriter> ReportWriters { get; } = [.. _services.GetServices<IReportFileWriter>()];

    /// <summary>False in the open build, which refuses email at intake and has no email converter.</summary>
    public static bool ReadsEmail => ReadableFormats.Refusal(MediaType.MessageRfc822) is null;

    public static IWorkbookDocument OpenWorkbook(string path)
    {
        return Workbooks.Open(File.OpenRead(path), Path.GetFileName(path));
    }

    public static IWorkbookDocument OpenWorkbook(byte[] workbookBytes, string fileName)
    {
        return Workbooks.Open(new MemoryStream(workbookBytes), fileName);
    }
}
