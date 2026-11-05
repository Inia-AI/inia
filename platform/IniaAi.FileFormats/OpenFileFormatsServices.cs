using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.FileFormats.Unlock;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
}
