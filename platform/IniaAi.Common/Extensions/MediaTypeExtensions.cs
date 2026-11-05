using IniaAi.Common.Enums;

namespace IniaAi.Common.Extensions;

public static class MediaTypeExtensions
{
    /// <summary>The IANA name to store the bytes under; an unknown type is stored as an octet stream.</summary>
    public static string ToContentType(this MediaType mediaType)
    {
        return (mediaType is MediaType.Unknown ? MediaType.ApplicationOctetStream : mediaType).GetEnumMember();
    }

    public static bool IsEmail(this MediaType mediaType)
    {
        return mediaType is MediaType.MessageRfc822 or MediaType.ApplicationVndMsOutlook;
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
