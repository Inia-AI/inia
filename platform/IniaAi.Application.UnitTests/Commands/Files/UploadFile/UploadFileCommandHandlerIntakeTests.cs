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
        Assert.Equal(".exe", document.FileMetadata!.Extension);
        _ = await _harness.FileStorageManager.Received(1).StoreFileAsync(
            Arg.Any<string>(), UploadFileHandlerHarness.ORGANIZATION_ID, Arg.Any<string>(), document.FileMetadata.Id, Arg.Any<Stream>(), "application/pdf");
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
