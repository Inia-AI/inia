using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Inspection.Checks;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Fixtures;
using System.Text;

namespace IniaAi.FileFormats.Tests.Inspection;

/// <summary>
/// The email chain admits an RFC 822 or Outlook message by its detected type and refuses
/// everything else, whatever the file is called: a type the platform never reads keeps the
/// general admission wording, a readable document that is not an email gets the email-only one.
/// </summary>
public class EmailMessageInspectorTests
{
    private const string PDF_MEDIA_TYPE = "application/pdf";

    private static FileInspection Inspect(byte[] bytes, string fileName)
    {
        return FileContentInspector.EmailMessages.Inspect(bytes, fileName, FileInspectionLimits.Default);
    }

    [Fact]
    public void AnRfc822Message_IsAdmitted_UnderAnyName()
    {
        byte[] message = Encoding.ASCII.GetBytes("From: sender@example.com\r\nSubject: Statement\r\n\r\nBody\r\n");

        FileInspection verdict = Inspect(message, "message.dat");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal("message/rfc822", verdict.DetectedMediaType);
    }

    [Fact]
    public void AnOutlookMessage_IsAdmitted()
    {
        FileInspection verdict = Inspect(HostileBytes.OutlookMessage(), "newsletter.msg");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
    }

    [Fact]
    public void APdf_IsRefused_WithTheEmailOnlyReason()
    {
        FileInspection verdict = Inspect(PdfFixtures.Minimal("statement"), "statement.eml");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("Only email files (.eml or .msg) are accepted here. Use the email itself, not one of its attachments.", verdict.Detail);
        Assert.Equal(PDF_MEDIA_TYPE, verdict.DetectedMediaType);
    }

    [Fact]
    public void AnExecutable_IsRefused_ForWhatItIs()
    {
        FileInspection verdict = Inspect(HostileBytes.WindowsExecutable(), "invoice.eml");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Contains("does not accept", verdict.Detail);
    }

    [Fact]
    public void AnEmptyFile_IsRefused()
    {
        FileInspection verdict = Inspect([], "empty.eml");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.EMPTY_FILE, verdict.ReasonCode);
    }
}
