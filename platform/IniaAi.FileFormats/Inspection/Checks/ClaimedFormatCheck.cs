using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// The filename never admits a file, but it may refuse one. A name that claims a format with a
/// signature (a PDF, a Word document, a spreadsheet, an image, an email) over bytes of another
/// family is the strongest sign the wrong file was attached, so the file is refused with both
/// named. Members of one family pass under each other's names, because a workbook saved as the
/// other Excel format is still the spreadsheet its sender meant. A name with no signed format,
/// or none at all, claims nothing.
/// </summary>
public sealed class ClaimedFormatCheck : IFileContentCheck
{
    public FileInspection? Run(FileInspectionContext context)
    {
        DetectedContent detected = context.RequireDetected();
        FormatFamily? claimed = SignedFamilyOf(Utils.DetermineMediaType(context.FileName));

        if (claimed is null || detected.PlatformMediaType is not MediaType mediaType || FamilyOf(mediaType) == claimed)
        {
            return null;
        }

        return FileInspection.Refuse(
            IntakeRefusalReasons.FILE_TYPE_MISMATCH,
            $"The file is named as {Describe(claimed.Value)}, but its content is {Describe(FamilyOf(mediaType))}. Check that it is the right file, or rename it to match its content.",
            detected);
    }

    private static FormatFamily? SignedFamilyOf(MediaType claimed)
    {
        FormatFamily family = FamilyOf(claimed);
        return TextContentSniffer.Yields(claimed) || family is FormatFamily.Other ? null : family;
    }

    private static FormatFamily FamilyOf(MediaType mediaType)
    {
        if (mediaType.IsEmail())
        {
            return FormatFamily.Email;
        }

        return mediaType switch
        {
            MediaType.ApplicationPdf => FormatFamily.Pdf,
            MediaType.ApplicationMsWord
                or MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument => FormatFamily.WordDocument,
            MediaType.ApplicationVndMsExcel
                or MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
                or MediaType.ApplicationVndMsExcelSheetMacroEnabled12 => FormatFamily.Spreadsheet,
            MediaType.ImageJpeg or MediaType.ImagePng => FormatFamily.Image,
            MediaType.TextPlain or MediaType.TextCsv or MediaType.TextMarkdown => FormatFamily.Text,
            _ => FormatFamily.Other,
        };
    }

    private static string Describe(FormatFamily family)
    {
        return family switch
        {
            FormatFamily.Pdf => "a PDF",
            FormatFamily.WordDocument => "a Word document",
            FormatFamily.Spreadsheet => "a spreadsheet",
            FormatFamily.Image => "an image",
            FormatFamily.Email => "an email",
            FormatFamily.Text => "plain text",
            _ => "another format",
        };
    }

    private enum FormatFamily
    {
        Pdf,
        WordDocument,
        Spreadsheet,
        Image,
        Email,
        Text,
        Other,
    }
}
