using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Documents;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.ContractTests;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Fixtures;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using NSubstitute;
using System.Diagnostics;
using System.Text;
using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.Application.UnitTests.Commands.Files.UploadFile;

/// <summary>
/// The API mouth against a real database: a refusal answers with its reason and leaves no
/// document, no file metadata and no blob; an admitted file is stored as the type its bytes
/// say, whatever its name claims; and the unlock gate sits behind the inspector, so the
/// compound-file amplifier never reaches a format library's detector. The refusal trail is the
/// recorder's concern and is tested with it.
/// </summary>
public class UploadFileCommandHandlerIntakeTests : IDisposable
{
    private const string USER_PASSWORD = "user-pw-1";
    private const string OWNER_PASSWORD = "owner-pw-1";

    private readonly UploadFileHandlerHarness _harness = new(new LoggingIntakeRefusalRecorder(NullLogger<LoggingIntakeRefusalRecorder>.Instance));

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task APdf_IsStoredWithItsCountedPages_AndTheReplySaysHowMany()
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(PdfFixtures.Pages("One", "Two", "Three"), "report.pdf");

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        Assert.Equal(3, result.Value.CountedPages);
        Document document = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, document.CountedPages);
    }

    [Fact]
    public async Task AnEmail_IsStoredWithItsBodyAndAttachmentPagesApart_AndItsIdentifier()
    {
        SkipUnlessTheBuildReadsEmail();
        var message = new MimeMessage { Subject = "Quarterly update", Date = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero) };
        message.From.Add(new MailboxAddress("Fund Ops", "ops@fundco.example"));
        message.To.Add(new MailboxAddress("Intake", "intake@inia.example"));
        var body = new BodyBuilder { TextBody = "Results attached." };
        _ = body.Attachments.Add("results.pdf", PdfFixtures.Pages("One", "Two", "Three"), ContentType.Parse("application/pdf"));
        message.Body = body.ToMessageBody();
        using var email = new MemoryStream();
        message.WriteTo(email, TestContext.Current.CancellationToken);

        ErrorOr<FileDto> result = await _harness.HandleAsync(email.ToArray(), "update.eml");

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        Assert.Equal((1, 3), (result.Value.CountedPages, result.Value.CountedAttachmentPages));
        Document stored = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((1, 3, "Quarterly_update_2026-11-01_10-00-00"), (stored.CountedPages, stored.CountedAttachmentPages, stored.EmailIdentifier));
    }

    [Fact]
    public async Task AnEmailThePlatformCannotCount_IsStillStored_WithNoCount()
    {
        SkipUnlessTheBuildReadsEmail();

        // The harness's email service has no Outlook converter, so it cannot open a .msg to count it.
        ErrorOr<FileDto> result = await _harness.HandleAsync(ContractFixture.Bytes(ContractFixture.OUTLOOK_MESSAGE), "newsletter.msg");

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        Document stored = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((null, null, null), (stored.CountedPages, stored.CountedAttachmentPages, stored.EmailIdentifier));
    }

    [Fact]
    public async Task AReusedDocumentWithoutACount_IsCountedWhenUploadedAgain()
    {
        byte[] pdf = PdfFixtures.Pages("One", "Two");
        _ = await _harness.HandleAsync(pdf, "report.pdf");
        Document stored = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        stored.CountedPages = null;
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<FileDto> again = await _harness.HandleAsync(pdf, "report.pdf");

        Assert.False(again.Value.Created);
        Assert.Equal(2, again.Value.CountedPages);
    }

    [Fact]
    public async Task AnEmail_IsStoredAsItsOriginalBytes()
    {
        SkipUnlessTheBuildReadsEmail();

        byte[] email = Encoding.ASCII.GetBytes(
            "From: ops@fundco.example\r\nTo: intake@inia.example\r\nSubject: Keep me raw\r\n\r\nBody text\r\n");
        byte[]? stored = null;
        _ = _harness.FileStorageManager.StoreFileAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<Stream>(stream => stored = ReadAll(stream)), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(3));

        ErrorOr<FileDto> result = await _harness.HandleAsync(email, "sample.eml");

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        Assert.Equal(email, stored);
        Document document = Assert.Single(await _harness.Context.Documents.Include(d => d.FileMetadata).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MediaType.MessageRfc822, document.MediaType);
        Assert.Equal(email.Length, document.FileMetadata!.SizeBytes);
    }

    [Fact]
    public async Task RenamedExecutable_IsRefusedWithItsReason_AndStoresNothing()
    {
        byte[] executable = HostileBytes.WindowsExecutable();

        ErrorOr<FileDto> result = await _harness.HandleAsync(executable, "report.pdf");

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(IntakeRefusalErrors.CODE_PREFIX + IntakeRefusalReasons.FILE_TYPE_MISMATCH, error.Code);
        Assert.Contains("accept", error.Description);
        Assert.True(IntakeRefusalErrors.IsRefusal(error));
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData(0, IntakeRefusalReasons.EMPTY_FILE)]
    [InlineData(2 * 1024 * 1024, IntakeRefusalReasons.FILE_TOO_LARGE)]
    public async Task EmptyOrOversizedFile_IsRefusedWithItsReason(int size, string reasonCode)
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(new byte[size], "statement.pdf");

        Assert.True(result.IsError);
        Assert.Equal(IntakeRefusalErrors.CODE_PREFIX + reasonCode, result.FirstError.Code);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task EncryptedPdf_WithItsPassword_IsAdmittedAsTheDecryptedCopy()
    {
        byte[] encrypted = PdfFixtures.Encrypted(MakePdf(), USER_PASSWORD, OWNER_PASSWORD);

        ErrorOr<FileDto> result = await _harness.HandleAsync(encrypted, "locked.pdf", USER_PASSWORD);

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.Include(d => d.FileMetadata).ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(document.WasPasswordProtected);
        Assert.Equal(MediaType.ApplicationPdf, document.MediaType);
        _ = Assert.Single(await _harness.Context.Set<DocumentUnlockAuditLog>().ToListAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(encrypted.Length, document.FileMetadata!.SizeBytes);
        _ = await _harness.FileStorageManager.Received(1).StoreFileAsync(
            Arg.Any<string>(), UploadFileHandlerHarness.ORGANIZATION_ID, Arg.Any<string>(), document.FileMetadata.Id, Arg.Any<Stream>(), "application/pdf");
    }

    [Fact]
    public async Task EncryptedPdf_WithoutPassword_AsksForOne_AndWritesNoRecord()
    {
        byte[] encrypted = PdfFixtures.Encrypted(MakePdf(), USER_PASSWORD, OWNER_PASSWORD);

        ErrorOr<FileDto> result = await _harness.HandleAsync(encrypted, "locked.pdf");

        Assert.True(result.IsError);
        Assert.Equal("File.Password.Required", result.FirstError.Code);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task OwnerOnlyPdf_WithoutPassword_IsAdmittedWithoutAPrompt()
    {
        byte[] restricted = PdfFixtures.Encrypted(MakePdf(), userPassword: string.Empty, OWNER_PASSWORD);

        ErrorOr<FileDto> result = await _harness.HandleAsync(restricted, "annual-report.pdf");

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MediaType.ApplicationPdf, document.MediaType);
        Assert.True(document.WasPasswordProtected);
    }

    [Fact]
    public async Task MutatedCompoundFileNamedXlsx_IsDecidedInUnderASecond()
    {
        byte[] mutated = HostileBytes.MutatedOutlookMessage();
        Assert.Empty(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        var stopwatch = Stopwatch.StartNew();

        ErrorOr<FileDto> result = await _harness.HandleAsync(mutated, "quarter.xlsx");

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"took {stopwatch.ElapsedMilliseconds} ms");
        Assert.NotEqual("File.Password.Required", result.IsError ? result.FirstError.Code : string.Empty);
    }

    [Fact]
    public async Task PdfNamedExe_IsStoredAsAPdf()
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(MakePdf(), "report.exe");

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.Include(d => d.FileMetadata).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MediaType.ApplicationPdf, document.MediaType);
        Assert.Equal(("report", ".pdf"), (document.FileMetadata!.Name, document.FileMetadata.Extension));
        _ = await _harness.FileStorageManager.Received(1).StoreFileAsync(
            Arg.Any<string>(), UploadFileHandlerHarness.ORGANIZATION_ID, Arg.Any<string>(), document.FileMetadata.Id, Arg.Any<Stream>(), "application/pdf");
    }

    [Theory]
    [InlineData("statement", "statement", ".pdf")]
    [InlineData("statement.PDF", "statement", ".PDF")]
    [InlineData("annual report.v2", "annual report", ".pdf")]
    public async Task APdf_IsStoredUnderAPdfsExtension_KeepingOneItAlreadyHas(string uploaded, string name, string extension)
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(MakePdf(), uploaded);

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.Include(d => d.FileMetadata).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((name, extension), (document.FileMetadata!.Name, document.FileMetadata.Extension));
    }

    [Theory]
    [InlineData("notes.md", ".md")]
    [InlineData("figures.csv", ".csv")]
    [InlineData("notes.txt", ".txt")]
    [InlineData("notes.exe", ".txt")]
    [InlineData("notes.log", ".txt")]
    public async Task AText_IsStoredUnderItsTypesExtension(string uploaded, string extension)
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync("Net assets are 1,234,567."u8.ToArray(), uploaded);

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.Include(d => d.FileMetadata).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(extension, document.FileMetadata!.Extension);
    }

    [Fact]
    public async Task APdfFollowedByAZip_IsStoredAsThePdfTheGateSaw()
    {
        byte[] zip;
        using (var stream = new MemoryStream())
        {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using Stream entry = archive.CreateEntry("hello.txt").Open();
                entry.Write("hello"u8);
            }

            zip = stream.ToArray();
        }

        ErrorOr<FileDto> result = await _harness.HandleAsync([.. MakePdf(), .. zip], "statement.pdf");

        Assert.False(result.IsError);
        Document document = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MediaType.ApplicationPdf, document.MediaType);
    }

    [Fact]
    public async Task TheSameFileUploadedTwice_ReusesTheDocument_AndReportsTheReuse()
    {
        byte[] pdf = MakePdf();

        ErrorOr<FileDto> first = await _harness.HandleAsync(pdf, "report.pdf");
        ErrorOr<FileDto> second = await _harness.HandleAsync(pdf, "report.pdf");

        Assert.True(first.Value.Created);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Id, second.Value.Id);
        _ = Assert.Single(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MacroWorkbook_IsAdmittedWithItsMacroMark_AndAPlainWorkbookWithout()
    {
        ErrorOr<FileDto> macro = await _harness.HandleAsync(OoxmlFixtures.MacroWorkbook(), "model.xlsm");
        ErrorOr<FileDto> plain = await _harness.HandleAsync(OoxmlFixtures.Workbook(), "plain.xlsx");

        Assert.False(macro.IsError);
        Assert.False(plain.IsError);
        List<Document> documents = await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(documents, d => d.Id == macro.Value.Id).HasMacros);
        Assert.False(Assert.Single(documents, d => d.Id == plain.Value.Id).HasMacros);
    }

    [Fact]
    public async Task OutlookMessageNamedXls_IsRefusedNamingBothFormats_AndStoresNothing()
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(HostileBytes.OutlookMessage(), "mail.xls");

        Error error = Assert.Single(result.Errors);
        Assert.Equal(IntakeRefusalErrors.CODE_PREFIX + IntakeRefusalReasons.FILE_TYPE_MISMATCH, error.Code);
        Assert.Equal("The file is named as a spreadsheet, but its content is an email. Check that it is the right file, or rename it to match its content.", error.Description);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task PlainTextNamedPdf_IsRefusedNamingBothFormats_AndStoresNothing()
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(
            Encoding.UTF8.GetBytes("This is just plain text content, not a PDF file at all."),
            "not-a-real-document.pdf");

        Error error = Assert.Single(result.Errors);
        Assert.Equal(IntakeRefusalErrors.CODE_PREFIX + IntakeRefusalReasons.FILE_TYPE_MISMATCH, error.Code);
        Assert.Equal("The file is named as a PDF, but its content is plain text. Check that it is the right file, or rename it to match its content.", error.Description);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task APdfUnderAProtectionNoPasswordRemoves_IsRefused_SayingWhereToRemoveIt()
    {
        ErrorOr<FileDto> result = await _harness.HandleAsync(ContractFixture.Bytes(ContractFixture.UNKNOWN_HANDLER_PDF), "certified.pdf");

        Error error = Assert.Single(result.Errors);
        Assert.Equal("File.Password.Unsupported", error.Code);
        Assert.Equal(
            "The file is protected in a way that a password cannot remove, such as a certificate. Remove the protection in the program that made it, then upload it again.",
            error.Description);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task ADamagedPdf_IsAdmitted_WithAWarningThatSaysWhatToDo()
    {
        byte[] whole = MakePdf();
        byte[] damaged = whole[..(whole.Length - 30)];

        ErrorOr<FileDto> result = await _harness.HandleAsync(damaged, "damaged.pdf");

        Assert.False(result.IsError);
        Assert.Equal(
            ["This PDF looks damaged. The platform repairs it while reading it, but some content may be missing. If the results look wrong, download it again from its source."],
            result.Value.Warnings);
    }

    private async Task AssertNothingStoredAsync()
    {
        Assert.Empty(await _harness.Context.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _harness.Context.FileMetadata.ToListAsync(TestContext.Current.CancellationToken));
        _ = await _harness.FileStorageManager.DidNotReceiveWithAnyArgs().StoreFileAsync(
            default(string)!, default!, default!, default!, default(Stream)!, default!);
    }

    private static byte[] MakePdf()
    {
        return PdfFixtures.Plain("Intake test content 12345");
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        stream.Position = 0;
        return copy.ToArray();
    }

    /// <summary>The open build refuses email at intake, so a test that admits one runs on the paid build only.</summary>
    private static void SkipUnlessTheBuildReadsEmail()
    {
        Assert.SkipUnless(FileFormatsUnderTest.ReadsEmail, "This build does not read email.");
    }
}
