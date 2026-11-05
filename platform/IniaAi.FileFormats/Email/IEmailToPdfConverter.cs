namespace IniaAi.FileFormats.Email;

/// <summary>
/// Turns an email into the PDF its body reads as. Only the paid build registers one; the open
/// build refuses email at intake, so it never reaches here.
/// </summary>
public interface IEmailToPdfConverter
{
    /// <summary>The Outlook message as an RFC 822 message.</summary>
    byte[] ConvertMsgToEml(byte[] msgBytes);

    EmailPdf ConvertToPdf(byte[] emlBytes);
}

/// <param name="ShowsContentImage">
/// True when a page of the body shows an image at least <see cref="MIN_CONTENT_IMAGE_WIDTH_PT"/> by
/// <see cref="MIN_CONTENT_IMAGE_HEIGHT_PT"/>, which can carry content only OCR reads, and when the
/// body cannot be checked: a missed table loses data, while an extra OCR call only costs the call.
/// </param>
public sealed record EmailPdf(byte[] PdfBytes, bool ShowsContentImage)
{
    /// <summary>
    /// The smallest shown width of a content image. A typical signature logo (180 x 60 px) shows at
    /// 135 x 45 pt and social icons at 18 pt, while a pasted table shrunk to half width still shows
    /// above 300 x 100 pt. A larger logo or a banner counts too: that costs an OCR call and loses nothing.
    /// </summary>
    public const double MIN_CONTENT_IMAGE_WIDTH_PT = 150;

    /// <inheritdoc cref="MIN_CONTENT_IMAGE_WIDTH_PT"/>
    public const double MIN_CONTENT_IMAGE_HEIGHT_PT = 50;
}
