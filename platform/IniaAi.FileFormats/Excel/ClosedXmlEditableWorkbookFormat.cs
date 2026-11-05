using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace IniaAi.FileFormats.Excel;

/// <summary>The open build's workbook writer, on ClosedXML, for .xlsx and .xlsm files.</summary>
public sealed class ClosedXmlEditableWorkbookFormat : IEditableWorkbookFormat
{
    private const string XLSX = ".xlsx";
    private const string XLSM = ".xlsm";
    private const string WORKBOOK_CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MACRO_WORKBOOK_CONTENT_TYPE = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    /// <summary>ClosedXML reads parts of the source package until it saves, so the workbook keeps the stream open.</summary>
    public IEditableWorkbook Open(byte[] workbookBytes)
    {
        var stream = new MemoryStream(workbookBytes, writable: false);
        try
        {
            return new ClosedXmlEditableWorkbook(new XLWorkbook(stream), stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public string? SaveExtension(byte[] workbookBytes)
    {
        try
        {
            using var stream = new MemoryStream(workbookBytes, writable: false);
            using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, isEditable: false);
            return document.WorkbookPart?.ContentType switch
            {
                WORKBOOK_CONTENT_TYPE => XLSX,
                MACRO_WORKBOOK_CONTENT_TYPE => XLSM,
                _ => null,
            };
        }
        catch (Exception e) when (OpenXmlPackageFailure.IsUnreadablePackage(e))
        {
            return null;
        }
    }
}
