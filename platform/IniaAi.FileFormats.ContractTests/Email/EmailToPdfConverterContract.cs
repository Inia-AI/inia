using IniaAi.Common.Enums;
using IniaAi.FileFormats.Email;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf;
using System.Text;
using System.Text.RegularExpressions;
using static IniaAi.FileFormats.ContractTests.ContractFixture;

namespace IniaAi.FileFormats.ContractTests.Email;

/// <summary>
/// What email expansion relies on from every <see cref="IEmailToPdfConverter"/>: an Outlook message
/// turned into an RFC 822 message, and a PDF that reads as the email's body. The PDF is read with the
/// <see cref="IPdfFormat"/> the subclass supplies.
/// </summary>
public abstract partial class EmailToPdfConverterContract
{
    private const string TEXT_ONLY_BODY = "The fund returned 1.24% net in September; NAV per share is 112.37 as of 30 September 2026.";
    private const string NEWSLETTER_BODY = "The Corvane Global Macro Fund returned 1.4% in December and 6.2% for the year.";
    private const string NEWSLETTER_SUBJECT = "Monthly Newsletter";
    private const string SUBJECT_HEADER = "Subject:";
    private const string INSPECTED_NAME = "email";
    private const string BODY_PDF_NAME = "email-body.pdf";

    protected abstract IEmailToPdfConverter Converter { get; }

    protected abstract IPdfFormat PdfReader { get; }

    [Fact]
    public void ConvertToPdf_OfAnRfc822Message_IsAPdfThatReadsAsItsBody()
    {
        EmailPdf pdf = Converter.ConvertToPdf(ContractFixture.Bytes(TEXT_ONLY_EML));

        Assert.Contains(TEXT_ONLY_BODY, ReadText(pdf));
    }

    [Fact]
    public async Task ConvertToPdf_FetchesNothingThatTheBodyNames()
    {
        using var probe = new LoopbackProbe();
        string message = "From: sender@example.test\r\nTo: reader@example.test\r\nSubject: Remote content\r\nMIME-Version: 1.0\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n\r\n"
            + $"<html><head>{RemoteContent.Head(probe.Address)}</head><body><p>{TEXT_ONLY_BODY}</p>{RemoteContent.Body(probe.Address)}</body></html>\r\n";
        IEmailToPdfConverter converter = Converter;

        _ = await Task.Run(() => converter.ConvertToPdf(Encoding.UTF8.GetBytes(message)))
            .WaitAsync(EntityPackages.ReadTimeLimit, TestContext.Current.CancellationToken);

        Assert.True(probe.Connections == 0, $"Fetched: {string.Join("; ", probe.Requests)}");
    }

    [Fact]
    public void ConvertMsgToEml_IsAnRfc822MessageWithTheSameHeadersAndBody()
    {
        IEmailToPdfConverter converter = Converter;

        byte[] eml = converter.ConvertMsgToEml(ContractFixture.Bytes(OUTLOOK_MESSAGE));

        Assert.Equal(MediaType.MessageRfc822, FileContentInspector.Default.Inspect(eml, INSPECTED_NAME, FileInspectionLimits.Default).MediaType);
        Assert.Equal(NEWSLETTER_SUBJECT, SubjectOf(eml));
        Assert.Contains(NEWSLETTER_BODY, ReadText(converter.ConvertToPdf(eml)));
    }

    [Theory]
    [InlineData(TEXT_ONLY_EML, false)]
    [InlineData(SIGNATURE_ONLY_EML, false)]
    [InlineData(PASTED_TABLE_EML, true)]
    public void ConvertToPdf_ShowsAContentImage_OnlyWhenTheBodyShowsMoreThanALogoOrASignature(string fixture, bool showsContentImage)
    {
        EmailPdf pdf = Converter.ConvertToPdf(ContractFixture.Bytes(fixture));

        Assert.Equal(showsContentImage, pdf.ShowsContentImage);
    }

    /// <summary>The Subject header, unfolded and trimmed: writers fold a header and case its name differently.</summary>
    private static string? SubjectOf(byte[] eml)
    {
        string message = Encoding.UTF8.GetString(eml);
        Match headerEnd = HeaderEnd().Match(message);
        string headers = FoldedLineBreak().Replace(headerEnd.Success ? message[..headerEnd.Index] : message, " ");

        return headers.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .FirstOrDefault(line => line.StartsWith(SUBJECT_HEADER, StringComparison.OrdinalIgnoreCase))?[SUBJECT_HEADER.Length..]
            .Trim();
    }

    [GeneratedRegex("\\r?\\n\\r?\\n")]
    private static partial Regex HeaderEnd();

    [GeneratedRegex("\\r?\\n[ \\t]+")]
    private static partial Regex FoldedLineBreak();

    private string ReadText(EmailPdf pdf)
    {
        Assert.True(PdfUtils.HasValidStructure(pdf.PdfBytes), "The email body is not a whole PDF file.");
        using IPdfDocument document = PdfReader.Open(new MemoryStream(pdf.PdfBytes), BODY_PDF_NAME);
        IEnumerable<string> pages = Enumerable.Range(1, document.PageCount).Select(pageNumber => document.Text(pageNumber));

        return ExtractedText.Words(string.Join(' ', pages));
    }
}
