using IniaAi.Common.Enums;

namespace IniaAi.Common.Extensions;

public static class MediaTypeExtensions
{
    /// <summary>The IANA name to store the bytes under; an unknown type is stored as an octet stream.</summary>
    public static string ToContentType(this MediaType mediaType)
    {
        return (mediaType is MediaType.Unknown ? MediaType.ApplicationOctetStream : mediaType).GetEnumMember();
    }

    /// <summary>The extension a file of the type is stored under, or null for a type the upload gate never admits.</summary>
    public static string? FileExtension(this MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.ApplicationPdf => ".pdf",
            MediaType.ImageJpeg => ".jpg",
            MediaType.ImagePng => ".png",
            MediaType.TextPlain => ".txt",
            MediaType.TextCsv => ".csv",
            MediaType.TextMarkdown => ".md",
            MediaType.ApplicationVndMsExcel => ".xls",
            MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet => ".xlsx",
            MediaType.ApplicationVndMsExcelSheetMacroEnabled12 => ".xlsm",
            MediaType.ApplicationMsWord => ".doc",
            MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument => ".docx",
            MediaType.MessageRfc822 => ".eml",
            MediaType.ApplicationVndMsOutlook => ".msg",
            _ => null,
        };
    }

    /// <summary>
    /// The extension a file of the type is stored and served under: <paramref name="extension"/> when it already
    /// names the type, such as <c>.jpeg</c> for a JPEG, and otherwise the type's own, so a PDF named
    /// <c>report.exe</c> is served as <c>report.pdf</c>.
    /// </summary>
    public static string ServedExtension(this MediaType mediaType, string? extension)
    {
        string given = extension ?? string.Empty;
        return Utils.DetermineMediaTypeFromExtension(given) == mediaType || mediaType.FileExtension() is not { } own ? given : own;
    }

    /// <summary><paramref name="fileName"/> with its extension replaced by <see cref="ServedExtension"/>.</summary>
    public static string ServedFileName(this MediaType mediaType, string fileName)
    {
        string extension = Path.GetExtension(fileName);
        string served = mediaType.ServedExtension(extension);
        return served == extension ? fileName : fileName[..^extension.Length] + served;
    }

    public static bool IsEmail(this MediaType mediaType)
    {
        return mediaType is MediaType.MessageRfc822 or MediaType.ApplicationVndMsOutlook;
    }

    /// <summary>An Excel workbook in the Open XML format, with or without macros.</summary>
    public static bool IsWorkbook(this MediaType mediaType)
    {
        return mediaType is MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet or MediaType.ApplicationVndMsExcelSheetMacroEnabled12;
    }

    /// <summary>
    /// The formats the document processing pipeline turns into pages. Email and legacy
    /// binary Word are stored but never paged, and every other type has no dispatch branch.
    /// </summary>
    public static bool ProducesPages(this MediaType mediaType)
    {
        return mediaType is MediaType.ApplicationPdf
            or MediaType.ImageJpeg
            or MediaType.ImagePng
            or MediaType.TextCsv
            or MediaType.ApplicationVndMsExcel
            or MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
            or MediaType.ApplicationVndMsExcelSheetMacroEnabled12
            or MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument
            or MediaType.TextPlain
            or MediaType.TextMarkdown;
    }
}
