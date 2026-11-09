using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Pages;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.FileFormats.Unlock;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.FileFormats;

public static class OpenFileFormatsServices
{
    /// <summary>
    /// Registers the file formats implemented in open code, which every build has. A build that registers its own
    /// implementations afterwards, such as the paid one, replaces these.
    /// </summary>
    public static IServiceCollection AddOpenFileFormats(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportFileWriter, MarkdownReportWriter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportFileWriter, OpenXmlWordReportWriter>());
        services.TryAddSingleton(provider => WorkerLimitFrom(provider.GetService<IOptions<PlatformSettings>>()?.Value.PdfReader));
        _ = services.AddSingleton<IDocumentPageCounter>(provider => new DocumentPageCounter(
            CountingPdfFormat(provider),
            provider.GetRequiredService<IWorkbookFormat>(),
            provider.GetRequiredService<ILogger<DocumentPageCounter>>()));
        return services
            .AddSingleton<IWorkbookFormat, ClosedXmlWorkbookFormat>()
            .AddSingleton<IEditableWorkbookFormat, ClosedXmlEditableWorkbookFormat>()
            .AddSingleton<IWordPageReader, OpenXmlWordPageReader>()
            .AddSingleton<IWorkbookToWordConverter, OpenXmlWorkbookToWordConverter>()
            .AddSingleton<IWordTemplateFormat, OpenXmlWordTemplateFormat>()
            .AddSingleton<IPdfFormat, PdfiumPdfFormat>()
            .AddSingleton<IDocumentUnlocker, OpenDocumentUnlocker>()
            .AddSingleton<IReadableFormats, OpenBuildFormats>();
    }

    private static PdfiumWorkerLimit WorkerLimitFrom(PdfReaderSettings? settings)
    {
        int maxWorkers = settings?.MaxWorkers ?? PdfReaderSettings.DEFAULT_MAX_WORKERS;
        return maxWorkers >= 1
            ? new PdfiumWorkerLimit(maxWorkers)
            : throw new InvalidOperationException($"PlatformSettings:PdfReader:MaxWorkers is {maxWorkers}, and the PDF reader needs at least 1 worker. Set it to 1 or more.");
    }

    /// <summary>
    /// The PDF reader that counts the pages of uploads: the open reader with workers of its own, so an upload never waits
    /// for a report to close a file, or the build's own reader when a build replaces the open one.
    /// </summary>
    private static IPdfFormat CountingPdfFormat(IServiceProvider provider)
    {
        IPdfFormat pdf = provider.GetRequiredService<IPdfFormat>();
        if (pdf is not PdfiumPdfFormat)
        {
            return pdf;
        }

        int maxWorkers = provider.GetService<IOptions<PlatformSettings>>()?.Value.PdfReader.MaxCountingWorkers ?? PdfReaderSettings.DEFAULT_MAX_COUNTING_WORKERS;
        return maxWorkers >= 1
            ? new PdfiumPdfFormat(new PdfiumWorkerLimit(maxWorkers))
            : throw new InvalidOperationException($"PlatformSettings:PdfReader:MaxCountingWorkers is {maxWorkers}, and counting pages needs at least 1 worker. Set it to 1 or more.");
    }
}
