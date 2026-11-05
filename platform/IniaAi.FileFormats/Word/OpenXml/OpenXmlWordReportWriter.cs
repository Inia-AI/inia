using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// The open build's report export, written with the Open XML SDK: the banner and the report header, each section with
/// its rule and title, the content's HTML through HtmlToOpenXml, inline images, tables, and each chart as the image
/// that <see cref="ReportFileOptions.ChartRenderService"/> draws or, failing that, as a table of its data. A grid
/// section lays its blocks out in borderless tables.
/// </summary>
public sealed class OpenXmlWordReportWriter : IReportFileWriter
{
    private const string EXTENSION = ".docx";
    private const MediaType MEDIA_TYPE = MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument;

    private readonly ILogger<OpenXmlWordReportWriter> _logger;

    public OpenXmlWordReportWriter(ILogger<OpenXmlWordReportWriter> logger)
    {
        _logger = logger;
    }

    public ExportFormat Format => ExportFormat.Word;

    public string Extension => EXTENSION;

    public string ContentType => MEDIA_TYPE.GetEnumMember();

    public async Task<BinaryContent> WriteAsync(IDocumentElement document, ReportFileOptions options)
    {
        byte[] docx = await Task.Run(() => WriteDocx(document, options));
        return new BinaryContent(new BinaryData(docx), MEDIA_TYPE);
    }

    private byte[] WriteDocx(IDocumentElement root, ReportFileOptions options)
    {
        using var output = new MemoryStream();
        using (var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            new OpenXmlReport(document, options.ChartRenderService, options.ReportLayout, options.ReportContext, options.FooterNotice, _logger).Build(root);
        }

        return output.ToArray();
    }
}
