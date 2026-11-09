using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.Logging;

namespace IniaAi.FileFormats.Pages;

/// <summary>
/// Counts a file's pages the way processing will page it, with no model call and without holding the file's text whole:
/// <list type="bullet">
/// <item>a PDF's pages through the build's own reader, and one page for a picture;</item>
/// <item>a text or Markdown file's pages from <see cref="TextPages"/>, the pager processing uses;</item>
/// <item>for a CSV or a workbook, the larger of the pages of <see cref="ExcelUtils.ROWS_PER_PAGE"/> rows processing
/// makes and its text in pages of <see cref="TextPages.PAGE_CHARACTERS"/>, so a page never hides more text than a page
/// holds;</item>
/// <item>a Word file's text in such pages, and one page for each distinct picture, which processing reads as a
/// scanned page.</item>
/// </list>
/// </summary>
public sealed class DocumentPageCounter : IDocumentPageCounter
{
    private readonly IPdfFormat _pdf;
    private readonly IWorkbookFormat _workbooks;
    private readonly ILogger<DocumentPageCounter> _logger;

    public DocumentPageCounter(IPdfFormat pdf, IWorkbookFormat workbooks, ILogger<DocumentPageCounter> logger)
    {
        _pdf = pdf;
        _workbooks = workbooks;
        _logger = logger;
    }

    public int? Count(byte[] bytes, MediaType mediaType, string fileName)
    {
        try
        {
            return mediaType switch
            {
                MediaType.ApplicationPdf => PdfPages(bytes, fileName),
                MediaType.ImageJpeg or MediaType.ImagePng => 1,
                MediaType.TextCsv => CsvFilePages(bytes),
                MediaType.TextPlain => TextFilePages(bytes, markdown: false),
                MediaType.TextMarkdown => TextFilePages(bytes, markdown: true),
                MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument => WordPages(bytes),
                MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
                    or MediaType.ApplicationVndMsExcelSheetMacroEnabled12 => WorkbookPages(bytes),
                MediaType.ApplicationVndMsExcel => LegacyWorkbookPages(bytes, fileName),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "The pages of an uploaded {MediaType} file could not be counted", mediaType);
            return null;
        }
    }

    private int PdfPages(byte[] bytes, string fileName)
    {
        using IPdfDocument document = _pdf.Open(new MemoryStream(bytes, writable: false), fileName);
        return document.PageCount;
    }

    private static int CsvFilePages(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        int pages = 0;
        long characters = 0;
        foreach (string page in CsvPages.Read(reader))
        {
            pages++;
            characters += page.Length;
        }

        return Math.Max(pages, TextPagesOf(characters));
    }

    private static int TextFilePages(byte[] bytes, bool markdown)
    {
        using (var start = new StreamReader(new MemoryStream(bytes, writable: false)))
        {
            if (start.ReadBlock(new char[TextPages.PAGE_CHARACTERS + 1]) <= TextPages.PAGE_CHARACTERS)
            {
                return 1;
            }
        }

        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        return Math.Max(1, TextPages.Read(reader, markdown).Count());
    }

    private static int WordPages(byte[] bytes)
    {
        (long characters, int pictures) = OfficePackageText.Word(bytes);
        return TextPagesOf(characters) + pictures;
    }

    private static int WorkbookPages(byte[] bytes)
    {
        (long characters, int rowPages) = OfficePackageText.Workbook(bytes);
        return Math.Max(rowPages, TextPagesOf(characters));
    }

    private int LegacyWorkbookPages(byte[] bytes, string fileName)
    {
        using IWorkbookDocument workbook = _workbooks.Open(new MemoryStream(bytes, writable: false), fileName);
        long characters = 0;
        int rowPages = 0;
        foreach (IWorkbookSheet sheet in workbook.Sheets.Where(sheet => sheet.IsVisible))
        {
            var measure = new SheetMeasure();
            int columns = ExcelUtils.GetMaxColumn(sheet);
            for (int row = 0; row <= sheet.MaxDataRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    measure.Add(row, sheet.CellText(row, column).Length);
                }
            }

            characters += measure.Characters;
            rowPages += measure.RowPages();
        }

        return Math.Max(rowPages, TextPagesOf(characters));
    }

    private static int TextPagesOf(long characters)
    {
        return (int)Math.Max(1, Math.Min(int.MaxValue, (characters + TextPages.PAGE_CHARACTERS - 1) / TextPages.PAGE_CHARACTERS));
    }
}
