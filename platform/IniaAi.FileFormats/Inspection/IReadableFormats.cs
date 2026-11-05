using IniaAi.Common.Constants;
using IniaAi.Common.Enums;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// The document formats this build reads. Intake refuses the others, even for an upload whose sender vouches for it,
/// because nothing after intake could read them.
/// </summary>
public interface IReadableFormats
{
    /// <summary>Null when the build reads <paramref name="mediaType"/>; otherwise why it does not, naming the format.</summary>
    string? Refusal(MediaType mediaType);
}

/// <summary>The open build reads no email and no legacy binary workbook: no free library it uses reads them.</summary>
public sealed class OpenBuildFormats : IReadableFormats
{
    public const string LEGACY_WORKBOOK_NOT_READ =
        $"The platform does not read Excel 97-2003 workbooks (.xls). Save the workbook as .xlsx, then upload it again. {ProductEditions.FULL_EDITION} reads Excel 97-2003 workbooks directly.";

    public string? Refusal(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.MessageRfc822 =>
                $"The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. {ProductEditions.FULL_EDITION} reads email files directly and extracts their attachments.",
            MediaType.ApplicationVndMsOutlook =>
                $"The platform does not read Outlook email files (.msg). Save the email as a PDF, or upload its attachments instead. {ProductEditions.FULL_EDITION} reads Outlook email files directly and extracts their attachments.",
            MediaType.ApplicationVndMsExcel => LEGACY_WORKBOOK_NOT_READ,
            _ => null,
        };
    }
}
