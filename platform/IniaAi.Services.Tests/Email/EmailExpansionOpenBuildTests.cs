using IniaAi.Configuration;
using IniaAi.FileFormats.Email;
using IniaAi.Services.Email;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace IniaAi.Services.Tests.Email;

public class EmailExpansionOpenBuildTests
{
    [Fact]
    public async Task ExpandAsync_WithNoEmailConverter_RefusesTheEmail_NamingTheBuild()
    {
        var expansion = new EmailExpansionService(
            Options.Create(new PlatformSettings()),
            FileFormatsUnderTest.Unlocker,
            NullLogger<EmailExpansionService>.Instance);
        byte[] email = Encoding.ASCII.GetBytes("From: ops@fundco.example\r\nTo: intake@inia.example\r\nSubject: Statement\r\n\r\nBody text\r\n");

        EmailConversionException failure = await Assert.ThrowsAsync<EmailConversionException>(
            () => expansion.ExpandAsync(email, TestContext.Current.CancellationToken));

        Assert.Equal("The platform does not read email files. Save the email as a PDF, then upload it with its attachments. The full edition reads email files directly and extracts their attachments.", failure.Message);
    }
}
