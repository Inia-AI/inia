using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Tests.Unlock;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Fixtures;
using System.Text;

namespace IniaAi.FileFormats.Tests.Inspection;

public class FileContentInspectorTests
{
    private const string PASSWORD = "open-sesame";
    private const string UNRECOGNISED_MEDIA_TYPE = "application/octet-stream";

    private static FileInspection Inspect(byte[] bytes, string fileName)
    {
        return FileContentInspector.Default.Inspect(bytes, fileName, FileInspectionLimits.Default);
    }

    private static long AllocatedBy(Func<FileInspection> inspection, out FileInspection verdict)
    {
        _ = Inspect([1], "warm-up.bin");
        long before = GC.GetAllocatedBytesForCurrentThread();
        verdict = inspection();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void RenamedExecutable_IsRefusedForWhatItIs()
    {
        FileInspection verdict = Inspect(HostileBytes.WindowsExecutable(), "report.pdf");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("application/vnd.microsoft.portable-executable", verdict.DetectedMediaType);
        Assert.Equal("The platform does not accept this type of file (application/vnd.microsoft.portable-executable). Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead.", verdict.Detail);
        Assert.Null(verdict.MediaType);
    }

    [Fact]
    public void PlainZipNamedDocx_IsRefusedAsZip()
    {
        FileInspection verdict = Inspect(InspectionFixtures.PlainZip(), "contract.docx");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("application/zip", verdict.DetectedMediaType);
        Assert.Equal(MediaType.ApplicationZip, verdict.MediaType);
    }

    [Theory]
    [InlineData("report.exe")]
    [InlineData("report")]
    [InlineData("report.csv")]
    public void PdfBytes_AreAdmittedAsPdf_WhateverTheNameSays(string fileName)
    {
        FileInspection verdict = Inspect(InspectionFixtures.Resource("SimpleTestSearchableWithTextOnImage.pdf"), fileName);

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, verdict.MediaType);
        Assert.Equal("application/pdf", verdict.DetectedMediaType);
    }

    [Fact]
    public void PlainTextNamedPdf_IsRefused_NamingWhatTheNameClaimsAndWhatTheContentIs()
    {
        FileInspection verdict = Inspect(
            Encoding.UTF8.GetBytes("This is just plain text content, not a PDF file at all."),
            "not-a-real-document.pdf");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("The file is named as a PDF, but its content is plain text. Check that it is the right file, or rename it to match its content.", verdict.Detail);
        Assert.Equal(MediaType.TextPlain, verdict.MediaType);
    }

    public static TheoryData<Func<byte[]>, string, string> ContentUnlikeItsName => new()
    {
        { () => PdfFixtures.Plain("statement"), "statement.docx", "The file is named as a Word document, but its content is a PDF. Check that it is the right file, or rename it to match its content." },
        { () => InspectionFixtures.PngClaimingSize(10, 10), "scan.pdf", "The file is named as a PDF, but its content is an image. Check that it is the right file, or rename it to match its content." },
        { () => Encoding.UTF8.GetBytes("a;b;c\n1;2;3\n"), "export.xls", "The file is named as a spreadsheet, but its content is plain text. Check that it is the right file, or rename it to match its content." },
        { () => OoxmlFixtures.LegacyWorkbook(), "book.msg", "The file is named as an email, but its content is a spreadsheet. Check that it is the right file, or rename it to match its content." },
        { () => Encoding.UTF8.GetBytes("just a caption"), "photo.jpg", "The file is named as an image, but its content is plain text. Check that it is the right file, or rename it to match its content." },
    };

    [Theory]
    [MemberData(nameof(ContentUnlikeItsName))]
    public void ContentOfAnotherFormat_UnderANameThatClaimsASignedFormat_IsRefused(Func<byte[]> bytes, string fileName, string detail)
    {
        FileInspection verdict = Inspect(bytes(), fileName);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal(detail, verdict.Detail);
    }

    public static TheoryData<Func<byte[]>, string, MediaType> ContentOfTheFamilyItsNameClaims => new()
    {
        { () => OoxmlFixtures.Workbook(), "book.xls", MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet },
        { () => OoxmlFixtures.LegacyWorkbook(), "book.xlsx", MediaType.ApplicationVndMsExcel },
        { () => InspectionFixtures.PngClaimingSize(10, 10), "scan.jpg", MediaType.ImagePng },
        { () => InspectionFixtures.Resource("Email", "Monthly Newsletter.msg"), "mail.msg", MediaType.ApplicationVndMsOutlook },
    };

    [Theory]
    [MemberData(nameof(ContentOfTheFamilyItsNameClaims))]
    public void ContentOfTheFormatFamilyItsNameClaims_IsAdmittedForWhatItIs(Func<byte[]> bytes, string fileName, MediaType expected)
    {
        FileInspection verdict = Inspect(bytes(), fileName);

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(expected, verdict.MediaType);
    }

    [Fact]
    public void LegacyWorkbookAndOutlookMessage_ShareTheCfbSignature_AndAreToldApart()
    {
        FileInspection workbook = Inspect(OoxmlFixtures.LegacyWorkbook(), "book");
        FileInspection message = Inspect(InspectionFixtures.Resource("Email", "Monthly Newsletter.msg"), "mail");

        Assert.Equal(FileInspectionOutcome.Accepted, workbook.Outcome);
        Assert.Equal(MediaType.ApplicationVndMsExcel, workbook.MediaType);
        Assert.False(workbook.HasMacros);
        Assert.Equal(FileInspectionOutcome.Accepted, message.Outcome);
        Assert.Equal(MediaType.ApplicationVndMsOutlook, message.MediaType);
    }

    [Fact]
    public void LegacyWordDocument_IsRefused_BecauseThePlatformDoesNotReadIt()
    {
        FileInspection verdict = Inspect(InspectionFixtures.LegacyWordDocument(), "memo.docx");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("application/msword", verdict.DetectedMediaType);
    }

    [Fact]
    public void ZipBomb_IsRefusedBeforeExpansion_AndAllocatesNothingProportionalToIt()
    {
        const long expanded = 16L * 1024 * 1024;
        byte[] bomb = HostileBytes.ZipBomb(expanded);
        Assert.True(bomb.Length < 256 * 1024, $"the bomb should be small on disk, was {bomb.Length} bytes");

        long allocated = AllocatedBy(() => Inspect(bomb, "report.docx"), out FileInspection verdict);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
        Assert.Equal("The file unpacks to far more data than its size suggests, so it cannot be opened. Save it again from the program that made it, then upload the new copy.", verdict.Detail);
        Assert.True(allocated < 1024 * 1024, $"inspecting a {expanded}-byte bomb allocated {allocated} bytes");
    }

    [Fact]
    public void ArchiveOfMoreEntriesThanTheLimit_IsRefusedWithoutReadingItsDirectory()
    {
        int entries = FileInspectionLimits.Default.MaxArchiveEntries + 1;
        byte[] archive = InspectionFixtures.ZipOfEmptyEntries(entries, string.Empty);

        long allocated = AllocatedBy(() => Inspect(archive, "report.xlsx"), out FileInspection verdict);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
        Assert.Equal("The file holds 10,001 items, more than the limit of 10,000. Split the content into smaller files, then upload them.", verdict.Detail);
        Assert.True(allocated < 64 * 1024, $"refusing a {entries}-entry archive allocated {allocated} bytes");
    }

    [Fact]
    public void PackageWithAnOversizedManifest_IsRefusedBeforeTheManifestIsRead()
    {
        byte[] package = InspectionFixtures.PackageWithManifestOf(4L * 1024 * 1024);

        long allocated = AllocatedBy(() => Inspect(package, "report.xlsx"), out FileInspection verdict);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
        Assert.Equal("The file's internal index is too large for a document, so it cannot be opened. Save it again from the program that made it, then upload the new copy.", verdict.Detail);
        Assert.True(allocated < 512 * 1024, $"refusing a package with a 4 MiB manifest allocated {allocated} bytes");
    }

    [Fact]
    public void CompoundFileWithAMutatedHeader_IsDecidedAtOnce()
    {
        byte[] mutated = HostileBytes.MutatedOutlookMessage();
        _ = Inspect([1], "warm-up.bin");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        FileInspection verdict = Inspect(mutated, "mail.msg");
        stopwatch.Stop();

        Assert.NotEqual(FileInspectionOutcome.Encrypted, verdict.Outcome);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"a mutated compound file took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TruncatedZip_IsRefusedAsMalformed()
    {
        FileInspection verdict = Inspect(InspectionFixtures.TruncatedZip(), "broken.xlsx");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.MALFORMED_FILE, verdict.ReasonCode);
        Assert.Equal("The file is damaged and cannot be opened. Save or download it again, then upload the new copy.", verdict.Detail);
    }

    [Fact]
    public void MacroWorkbook_IsAdmitted_AndItsMacrosAreReported()
    {
        FileInspection verdict = Inspect(InspectionFixtures.Resource("CreditReviewTemplate.xlsm"), "credit.xlsm");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.ApplicationVndMsExcelSheetMacroEnabled12, verdict.MediaType);
        Assert.True(verdict.HasMacros);
    }

    [Fact]
    public void PlainWorkbook_ReportsNoMacros()
    {
        FileInspection verdict = Inspect(InspectionFixtures.Resource("test.xlsx"), "test.xlsx");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet, verdict.MediaType);
        Assert.False(verdict.HasMacros);
    }

    [Fact]
    public void EncryptedWorkbook_IsReportedEncrypted_NotRefused()
    {
        FileInspection verdict = Inspect(OoxmlFixtures.Workbook(PASSWORD), "book.xlsx");

        Assert.Equal(FileInspectionOutcome.Encrypted, verdict.Outcome);
        Assert.Equal(EncryptedContainer.OfficePackage, verdict.Container);
        Assert.Null(verdict.ReasonCode);
    }

    [Fact]
    public void EncryptedLegacyWorkbook_IsReportedEncrypted()
    {
        FileInspection verdict = Inspect(OoxmlFixtures.EncryptedLegacyWorkbook(), "book.xls");

        Assert.Equal(FileInspectionOutcome.Encrypted, verdict.Outcome);
        Assert.Equal(EncryptedContainer.LegacyWorkbook, verdict.Container);
        Assert.Equal(MediaType.ApplicationVndMsExcel, verdict.MediaType);
    }

    [Fact]
    public void EncryptedWordDocument_IsReportedEncrypted()
    {
        FileInspection verdict = Inspect(ContractFixture.Bytes(ContractFixture.PROTECTED_WORD_DOCUMENT), "memo.docx");

        Assert.Equal(FileInspectionOutcome.Encrypted, verdict.Outcome);
        Assert.Equal(EncryptedContainer.OfficePackage, verdict.Container);
    }

    [Fact]
    public void EncryptedPdf_IsReportedEncrypted()
    {
        byte[] plain = PdfFixtures.Plain("secret");
        byte[] encrypted = PdfFixtures.Encrypted(plain, PASSWORD, "owner");

        FileInspection verdict = Inspect(encrypted, "statement.pdf");

        Assert.Equal(FileInspectionOutcome.Encrypted, verdict.Outcome);
        Assert.Equal(EncryptedContainer.Pdf, verdict.Container);
        Assert.Equal(MediaType.ApplicationPdf, verdict.MediaType);
        Assert.Equal(FileInspectionOutcome.Accepted, Inspect(plain, "statement.pdf").Outcome);
    }

    public static TheoryData<string, MediaType> SupportedFixtures => new()
    {
        { Path.Combine("SimpleTestSearchableWithTextOnImage.pdf"), MediaType.ApplicationPdf },
        { Path.Combine("Inspection", "photo.jpg"), MediaType.ImageJpeg },
        { Path.Combine("Inspection", "screenshot.png"), MediaType.ImagePng },
        { Path.Combine("Inspection", "counterparties.csv"), MediaType.TextCsv },
        { Path.Combine("SimpleTest.docx"), MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument },
        { Path.Combine("Inspection", "notes.txt"), MediaType.TextPlain },
        { Path.Combine("Inspection", "notes.md"), MediaType.TextMarkdown },
        { Path.Combine("Email", "Monthly Newsletter.eml"), MediaType.MessageRfc822 },
    };

    [Theory]
    [MemberData(nameof(SupportedFixtures))]
    public void EveryFormatThePlatformSupports_IsAdmittedWithItsType(string relativePath, MediaType expected)
    {
        byte[] bytes = InspectionFixtures.Resource(relativePath);

        FileInspection verdict = Inspect(bytes, Path.GetFileName(relativePath));

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(expected, verdict.MediaType);
    }

    [Fact]
    public void EveryFixtureInTheCorpus_WithAnAcceptedExtension_IsAdmitted()
    {
        InspectionFixtures.SweepResult sweep = InspectionFixtures.Sweep(InspectionFixtures.ResourcesRoot, int.MaxValue, long.MaxValue);

        Assert.True(sweep.Inspected >= 30, $"expected the corpus, inspected {sweep.Inspected} files");
        Assert.Empty(sweep.FalseRefusals);
        Assert.Empty(sweep.Encrypted);
    }

    [Fact]
    public void UnsupportedImageWithASupportedExtension_IsRefused()
    {
        FileInspection verdict = Inspect(InspectionFixtures.GifHeader(), "scan.png");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal("image/gif", verdict.DetectedMediaType);
        Assert.Equal("The platform does not accept this type of file (image/gif). Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead.", verdict.Detail);
    }

    [Fact]
    public void BinaryJunkWithATextExtension_IsRefusedAsUnrecognised()
    {
        FileInspection verdict = Inspect(InspectionFixtures.BinaryJunk(4096), "data.csv");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, verdict.ReasonCode);
        Assert.Equal(UNRECOGNISED_MEDIA_TYPE, verdict.DetectedMediaType);
        Assert.Equal("The platform cannot tell what type of file this is. Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead.", verdict.Detail);
        Assert.Null(verdict.MediaType);
    }

    [Fact]
    public void TextInALegacyWindowsEncoding_IsStillAdmitted()
    {
        FileInspection verdict = Inspect(InspectionFixtures.Windows1252Text(), "names.csv");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.TextCsv, verdict.MediaType);
    }

    [Theory]
    [InlineData("Date:2026-01-01\r\n\r\n1;2;3\r\n", "ledger.csv")]
    [InlineData("Subject:Q3 summary\n\nRevenue grew.\n", "summary.txt")]
    public void TextWithOneHeaderLikeLine_ButNoSender_IsNotMail(string content, string fileName)
    {
        FileInspection verdict = Inspect(Encoding.UTF8.GetBytes(content), fileName);

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.NotEqual(MediaType.MessageRfc822, verdict.MediaType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Utf16TextWithAByteOrderMark_IsAdmittedAsText(bool bigEndian)
    {
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("name;city\r\nR\u00e9my;Gen\u00e8ve\r\n")];

        FileInspection verdict = Inspect(bytes, "names.csv");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.TextCsv, verdict.MediaType);
    }

    [Fact]
    public void ImageWhoseHeaderCannotBeRead_IsRefusedAsMalformed()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[32]];

        FileInspection verdict = Inspect(png, "scan.png");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.MALFORMED_FILE, verdict.ReasonCode);
        Assert.Equal("The image is damaged and cannot be opened. Save it again, then upload the new copy.", verdict.Detail);
    }

    [Fact]
    public void ArchiveDeclaringMoreContentThanTheLimit_IsRefused()
    {
        var limits = FileInspectionLimits.Default with { MaxExpandedBytes = 8 };

        FileInspection verdict = FileContentInspector.Default.Inspect(InspectionFixtures.Resource("test.xlsx"), "test.xlsx", limits);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
        Assert.Matches(@"^The file unpacks to [0-9.]+ KB, more than the limit of 8 bytes\. Split the content into smaller files, then upload them\.$", verdict.Detail);
    }

    [Fact]
    public void TextWithoutMailHeaders_NamedEml_IsAdmittedAsPlainText()
    {
        FileInspection verdict = Inspect(Encoding.UTF8.GetBytes("just a note\nno headers here\n"), "note.eml");

        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.TextPlain, verdict.MediaType);
    }

    [Fact]
    public void EmptyFile_IsRefused()
    {
        FileInspection verdict = Inspect([], "empty.pdf");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.EMPTY_FILE, verdict.ReasonCode);
        Assert.Equal("The file is empty. Check that it is the right file, then upload it again.", verdict.Detail);
    }

    [Fact]
    public void TheSizeLimit_ComesFromTheDeploymentSettings()
    {
        var settings = new IniaAi.Configuration.Platform.IniaAiSettings { MaxFileSizeMb = 20 };

        FileInspectionLimits limits = FileInspectionLimits.FromSettings(settings);

        Assert.Equal(20L * 1024 * 1024, limits.MaxFileBytes);
        Assert.Equal(FileInspectionLimits.Default.MaxArchiveEntries, limits.MaxArchiveEntries);
    }

    [Fact]
    public void FileOverTheSizeLimit_IsRefused()
    {
        var limits = FileInspectionLimits.Default with { MaxFileBytes = 16 };

        FileInspection verdict = FileContentInspector.Default.Inspect(new byte[17], "big.pdf", limits);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TOO_LARGE, verdict.ReasonCode);
        Assert.Equal("The file is 17 bytes, more than the limit of 16 bytes. Upload a smaller copy, or split the document into smaller files.", verdict.Detail);
    }

    [Fact]
    public void ImageDeclaringMorePixelsThanTheLimit_IsRefusedWithoutDecodingIt()
    {
        byte[] png = InspectionFixtures.PngClaimingSize(40_000, 40_000);

        long allocated = AllocatedBy(() => Inspect(png, "huge.png"), out FileInspection verdict);

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
        Assert.Equal("The image is 40,000 by 40,000 pixels, more than the limit of 100,000,000 pixels. Make it smaller, then upload it again.", verdict.Detail);
        Assert.True(allocated < 4 * 1024 * 1024, $"refusing a 1.6-gigapixel image allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void EachAdmittedImageType_DeclaringMorePixelsThanTheLimit_IsRefused(string type)
    {
        byte[] image = type == "png" ? InspectionFixtures.PngClaimingSize(20_000, 20_000) : InspectionFixtures.JpegClaimingSize(20_000, 20_000);

        FileInspection verdict = Inspect(image, $"huge.{type}");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.STRUCTURAL_LIMIT, verdict.ReasonCode);
    }

    [Fact]
    public void APdfFollowedByAZip_IsOneType_AdmittedAsAPdf_AndRefusedUnderAWordName()
    {
        byte[] polyglot = [.. PdfFixtures.Minimal("x"), .. InspectionFixtures.PlainZip()];

        FileInspection asPdf = Inspect(polyglot, "a.pdf");
        FileInspection asWord = Inspect(polyglot, "a.docx");

        Assert.Equal(FileInspectionOutcome.Accepted, asPdf.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, asPdf.MediaType);
        Assert.Equal(FileInspectionOutcome.Refused, asWord.Outcome);
        Assert.Equal(IntakeRefusalReasons.FILE_TYPE_MISMATCH, asWord.ReasonCode);
    }

    [Fact]
    public void ACheckAppendedToTheSequence_RunsWithoutChangingTheVerdictType()
    {
        var inspector = new FileContentInspector([.. FileContentInspector.DefaultChecks, new RefuseMarkdownCheck()]);

        FileInspection markdown = inspector.Inspect(InspectionFixtures.Resource("Inspection", "notes.md"), "notes.md", FileInspectionLimits.Default);
        FileInspection text = inspector.Inspect(InspectionFixtures.Resource("Inspection", "notes.txt"), "notes.txt", FileInspectionLimits.Default);

        Assert.Equal(FileInspectionOutcome.Refused, markdown.Outcome);
        Assert.Equal(RefuseMarkdownCheck.REASON, markdown.ReasonCode);
        Assert.Equal(FileInspectionOutcome.Accepted, text.Outcome);
    }

    [Fact]
    public void ACheckThatReadsTheDetectedType_PlacedBeforeDetection_FailsLoudly()
    {
        var inspector = new FileContentInspector([new RefuseMarkdownCheck(), .. FileContentInspector.DefaultChecks]);

        _ = Assert.Throws<InvalidOperationException>(() => inspector.Inspect(InspectionFixtures.Resource("Inspection", "notes.md"), "notes.md", FileInspectionLimits.Default));
    }

    [Fact]
    public void PdfHeaderWithNothingBehindIt_IsRefusedAsMalformed()
    {
        byte[] stub = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 60) + "\n");

        FileInspection verdict = Inspect(stub, "fake-document.pdf");

        Assert.Equal(FileInspectionOutcome.Refused, verdict.Outcome);
        Assert.Equal(IntakeRefusalReasons.MALFORMED_FILE, verdict.ReasonCode);
        Assert.Equal("The PDF has no content and cannot be opened. Save or download it again, then upload the new copy.", verdict.Detail);
        Assert.Equal(MediaType.ApplicationPdf, verdict.MediaType);
    }

    [Fact]
    public void PdfWithObjectsButNoTrailer_IsStillAdmitted()
    {
        byte[] whole = InspectionFixtures.Resource("SimpleTestSearchableWithTextOnImage.pdf");
        byte[] truncated = whole[..(whole.Length - 512)];

        FileInspection verdict = Inspect(truncated, "damaged.pdf");

        Assert.False(PdfUtils.HasValidStructure(truncated));
        Assert.Equal(FileInspectionOutcome.Accepted, verdict.Outcome);
        Assert.Equal(MediaType.ApplicationPdf, verdict.MediaType);
    }

    [Theory]
    [InlineData("[Content_Types].xml")]
    [InlineData("word/document.xml")]
    public void APackageWhosePartNamesAnExternalDtd_IsInspectedWithoutFetchingIt(string part)
    {
        using var probe = new LoopbackProbe();

        FileInspection verdict = Inspect(EntityPackages.NamingAnExternalDtd(ContractFixture.Bytes(ContractFixture.TEN_PAGES_WORD_DOCUMENT), part, probe.Address), "entities.docx");

        Assert.Equal(0, probe.Connections);
        TestContext.Current.TestOutputHelper?.WriteLine($"{part}: {verdict.Outcome} {verdict.ReasonCode}");
    }

    private sealed class RefuseMarkdownCheck : IFileContentCheck
    {
        public const string REASON = "MarkdownNotWanted";

        public FileInspection? Run(FileInspectionContext context)
        {
            DetectedContent detected = context.RequireDetected();
            return detected.PlatformMediaType == MediaType.TextMarkdown
                ? FileInspection.Refuse(REASON, "Markdown is not wanted here.", detected)
                : null;
        }
    }
}
