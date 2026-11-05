using IniaAi.Application.Documents;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;
using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.Application.UnitTests.Documents;

/// <summary>
/// The gate itself: what each unlock answer becomes, that a false encryption verdict still
/// passes through admission, that the screening's typed accessors refuse to answer for a file
/// that was not admitted, that a file over the limit by its declared size is refused before
/// its bytes are read, and that a mouth whose sender vouches for the file keeps only the refusals
/// that protect the server. The refusal trail is the recorder's concern and is tested with it.
/// </summary>
public class DocumentIntakeGateTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-gate";
    private const string USER_PASSWORD = "user-pw-1";
    private const string OWNER_PASSWORD = "owner-pw-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly DocumentIntakeGate _gate;
    private readonly IntakeMouth _mouth;

    public DocumentIntakeGateTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _gate = Gate(FileFormatsUnderTest.ReadableFormats);
        _mouth = new IntakeMouth(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>(), ORGANIZATION_ID, WorkflowExecutionId: "exec-1");
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PdfThatOnlyMentionsEncrypt_IsAdmittedAsPdf()
    {
        byte[] pdf = [.. MakePdf(), .. Encoding.ASCII.GetBytes("\n%/Encrypt\n")];

        IntakeScreening screening = await Screen(pdf, "notes.pdf");

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, screening.MediaType);
        Assert.Same(pdf, screening.Bytes);
        Assert.Equal(FileProtectionStatus.NotEncrypted, screening.Unlock!.Status);
    }

    [Fact]
    public async Task EncryptedPdf_WithoutPassword_IsPasswordRequired()
    {
        IntakeScreening screening = await Screen(EncryptPdf(MakePdf()), "locked.pdf");

        Assert.Equal(IntakeOutcome.PasswordRequired, screening.Outcome);
    }

    [Fact]
    public async Task EncryptedPdf_WithWrongPassword_IsPasswordIncorrect()
    {
        IntakeScreening screening = await Screen(EncryptPdf(MakePdf()), "locked.pdf", "not-the-password");

        Assert.Equal(IntakeOutcome.PasswordIncorrect, screening.Outcome);
    }

    [Fact]
    public async Task EncryptedPdf_WithItsPassword_IsAdmittedWithTheDecryptedCopy()
    {
        byte[] encrypted = EncryptPdf(MakePdf());

        IntakeScreening screening = await Screen(encrypted, "locked.pdf", USER_PASSWORD);

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.NotEqual(encrypted, screening.Bytes);
        Assert.True(screening.Unlock!.WasPasswordProtected);
    }

    [Fact]
    public async Task RefusedBytes_CarryTheirReason_AndAnswerNoTypedAccessor()
    {
        IntakeScreening screening = await Screen(HostileBytes.WindowsExecutable(), "invoice.pdf");

        Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, screening.ReasonCode);
        Assert.Contains("accept", screening.Detail);
        _ = Assert.Throws<InvalidOperationException>(() => screening.MediaType);
        _ = Assert.Throws<InvalidOperationException>(() => screening.ContentType);
    }

    [Fact]
    public async Task AFileOverTheLimitByItsDeclaredSize_IsRefusedBeforeItsBytesAreRead()
    {
        long declaredSize = (1024L * 1024) + 1;

        IntakeScreening screening = await _gate.ScreenAsync(
            "huge.pdf",
            declaredSize,
            _ => throw new InvalidOperationException("The bytes were read."),
            password: null,
            _mouth,
            FileContentInspector.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TOO_LARGE, screening.ReasonCode);
        Assert.Empty(screening.Bytes);
    }

    [Fact]
    public async Task AFileWithinTheLimitByItsDeclaredSize_IsReadAndScreened()
    {
        byte[] pdf = MakePdf();

        IntakeScreening screening = await _gate.ScreenAsync(
            "small.pdf",
            pdf.Length,
            _ => Task.FromResult(pdf),
            password: null,
            _mouth,
            FileContentInspector.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, screening.MediaType);
        Assert.Same(pdf, screening.Bytes);
    }

    [Fact]
    public async Task AVouchedMouth_AdmitsBytesTheGateWouldRefuse_AsTheTypeTheirNameClaims()
    {
        IntakeScreening screening = await ScreenVouched(HostileBytes.WindowsExecutable(), "invoice.pdf");

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, screening.MediaType);
    }

    [Fact]
    public async Task AVouchedMouth_AdmitsAFileUnderItsWrongName_AsTheTypeItsBytesAre()
    {
        IntakeScreening screening = await ScreenVouched(OoxmlFixtures.Workbook(), "report.pdf");

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.Equal(MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet, screening.MediaType);
    }

    [Fact]
    public async Task AVouchedMouth_StillRefusesAFileNeitherItsBytesNorItsNameGiveATypeFor()
    {
        IntakeScreening screening = await ScreenVouched(HostileBytes.WindowsExecutable(), "tool.bin");

        Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, screening.ReasonCode);
    }

    [Fact]
    public async Task AVouchedMouth_AdmitsAnEmptyFile()
    {
        IntakeScreening screening = await ScreenVouched([], "blank.docx");

        Assert.Equal(IntakeOutcome.Admitted, screening.Outcome);
        Assert.Equal(MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, screening.MediaType);
    }

    [Fact]
    public async Task AVouchedMouth_StillRefusesAnArchiveThatExpandsPastTheLimits()
    {
        IntakeScreening screening = await ScreenVouched(HostileBytes.ZipBomb(16L * 1024 * 1024), "report.docx");

        Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, screening.ReasonCode);
    }

    [Fact]
    public async Task AVouchedMouth_StillRefusesAFileOverTheLimitByItsDeclaredSize()
    {
        IntakeScreening screening = await _gate.ScreenAsync(
            "huge.pdf",
            (1024L * 1024) + 1,
            _ => throw new InvalidOperationException("The bytes were read."),
            password: null,
            _mouth with { ContentIsVouchedFor = true },
            FileContentInspector.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TOO_LARGE, screening.ReasonCode);
    }

    public static TheoryData<byte[], string, string> FormatsTheOpenBuildDoesNotRead => new()
    {
        { ContractFixture.Bytes(ContractFixture.TEXT_ONLY_EML), "update.eml", "The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. The full edition reads email files directly and extracts their attachments." },
        { ContractFixture.Bytes(ContractFixture.OUTLOOK_MESSAGE), "update.msg", "The platform does not read Outlook email files (.msg). Save the email as a PDF, or upload its attachments instead. The full edition reads Outlook email files directly and extracts their attachments." },
        { OoxmlFixtures.LegacyWorkbook(), "figures.xls", "The platform does not read Excel 97-2003 workbooks (.xls). Save the workbook as .xlsx, then upload it again. The full edition reads Excel 97-2003 workbooks directly." },
        { OoxmlFixtures.EncryptedLegacyWorkbook(), "locked-figures.xls", "The platform does not read Excel 97-2003 workbooks (.xls). Save the workbook as .xlsx, then upload it again. The full edition reads Excel 97-2003 workbooks directly." },
    };

    [Theory]
    [MemberData(nameof(FormatsTheOpenBuildDoesNotRead))]
    public async Task TheOpenBuild_RefusesAFormatItDoesNotRead_EvenFromAVouchedMouth_AndBeforeAnyUnlock(byte[] bytes, string fileName, string refusal)
    {
        DocumentIntakeGate openBuild = Gate(new OpenBuildFormats());

        foreach (IntakeMouth mouth in new[] { _mouth, _mouth with { ContentIsVouchedFor = true } })
        {
            IntakeScreening screening = await openBuild.ScreenAsync(bytes, fileName, password: null, mouth, TestContext.Current.CancellationToken);

            Assert.Equal(IntakeOutcome.Refused, screening.Outcome);
            Assert.Equal(IntakeRefusalReasons.FORMAT_NOT_READ, screening.ReasonCode);
            Assert.Equal(refusal, screening.Detail);
        }
    }

    private static DocumentIntakeGate Gate(IReadableFormats formats)
    {
        return new DocumentIntakeGate(
            new LoggingIntakeRefusalRecorder(NullLogger<LoggingIntakeRefusalRecorder>.Instance),
            FileFormatsUnderTest.Unlocker,
            formats,
            Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = 1 } }));
    }

    private async Task<IntakeScreening> Screen(byte[] bytes, string fileName, string? password = null)
    {
        return await _gate.ScreenAsync(bytes, fileName, password, _mouth, TestContext.Current.CancellationToken);
    }

    private async Task<IntakeScreening> ScreenVouched(byte[] bytes, string fileName)
    {
        return await _gate.ScreenAsync(bytes, fileName, password: null, _mouth with { ContentIsVouchedFor = true }, TestContext.Current.CancellationToken);
    }

    private static byte[] MakePdf()
    {
        return PdfFixtures.Plain("Gate test content 12345");
    }

    private static byte[] EncryptPdf(byte[] plain)
    {
        return PdfFixtures.Encrypted(plain, USER_PASSWORD, OWNER_PASSWORD);
    }
}
