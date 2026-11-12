using IniaAi.Common.Enums;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;
using System.Text;
using System.Text.RegularExpressions;
using static IniaAi.FileFormats.ContractTests.ContractFixture;

namespace IniaAi.FileFormats.ContractTests.Unlock;

/// <summary>
/// What the intake gate relies on from every <see cref="IDocumentUnlocker"/>: the
/// <see cref="FileUnlockResult"/> each <see cref="FileProtectionStatus"/> names, and a decrypted copy
/// that the gate's second inspection admits as a plain file of the same type. A legacy workbook
/// (<see cref="EncryptedContainer.LegacyWorkbook"/>) is outside the contract: the open build does
/// not support the binary .xls format.
/// </summary>
public abstract partial class DocumentUnlockerContract
{
    private const string PDF_PASSWORD = "contract-open-pw";
    private const string WORKBOOK_PASSWORD = "contract-workbook-pw";
    private const string WORD_PASSWORD = "contract-word-pw";
    private const string WRONG_PASSWORD = "not-the-password";

    private const string WORKBOOK_CELL = "unlocked cell value";
    private const string WORD_PARAGRAPH = "unlocked paragraph text";
    private const string PASSWORD_IN_COPY = "The decrypted copy carries the password.";

    protected abstract IDocumentUnlocker Unlocker { get; }

    [Theory]
    [InlineData(PROTECTED_PDF)]
    [InlineData(PROTECTED_PDF_AES256)]
    public async Task APdf_WithItsPassword_IsDecrypted_IntoACopyWithNoEncryptionEntry(string fixture)
    {
        FileUnlockResult result = await UnlockAsync(fixture, PDF_PASSWORD);

        AssertDecrypted(result, fixture, MediaType.ApplicationPdf, PDF_PASSWORD);
        Assert.False(result.ContainedSignature);
    }

    [Fact]
    public async Task APdf_WithOnlyAnOwnerPassword_IsDecryptedWithoutOne()
    {
        FileUnlockResult result = await UnlockAsync(OWNER_RESTRICTED_PDF, password: null);

        AssertDecrypted(result, OWNER_RESTRICTED_PDF, MediaType.ApplicationPdf, password: null);
    }

    [Fact]
    public async Task ASignedPdf_IsDecrypted_AndDisclosesItsSignature()
    {
        FileUnlockResult result = await UnlockAsync(SIGNED_PDF, PDF_PASSWORD);

        AssertDecrypted(result, SIGNED_PDF, MediaType.ApplicationPdf, PDF_PASSWORD);
        Assert.True(result.ContainedSignature);
    }

    [Theory]
    [InlineData(PROTECTED_PDF)]
    [InlineData(PROTECTED_PDF_AES256)]
    public async Task APdf_WithAWrongPassword_IsPasswordIncorrect(string fixture)
    {
        AssertLocked(FileProtectionStatus.PasswordIncorrect, await UnlockAsync(fixture, WRONG_PASSWORD));
    }

    [Theory]
    [InlineData(PROTECTED_PDF)]
    [InlineData(PROTECTED_PDF_AES256)]
    public async Task APdf_WithNoPassword_IsPasswordRequired(string fixture)
    {
        AssertLocked(FileProtectionStatus.PasswordRequired, await UnlockAsync(fixture, password: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PDF_PASSWORD)]
    public async Task APdf_UnderASecurityHandlerThatTakesNoPassword_IsUnsupportedProtection_WithOrWithoutAPassword(string? password)
    {
        AssertLocked(FileProtectionStatus.UnsupportedProtection, await UnlockAsync(UNKNOWN_HANDLER_PDF, password));
    }

    [Fact]
    public async Task APdf_ThatOnlyNamesTheEncryptKeyInItsText_IsNotEncrypted()
    {
        FileUnlockResult result = await UnlockAsync(NAMES_ENCRYPT_PDF, PDF_PASSWORD);

        Assert.Equal(FileUnlockResult.NotEncrypted(), result);
    }

    [Theory]
    [InlineData(PROTECTED_WORKBOOK)]
    [InlineData(PROTECTED_WORKBOOK_AGILE)]
    public async Task AWorkbook_WithItsPassword_IsDecrypted_IntoAPlainPackageThatKeepsItsCells(string fixture)
    {
        FileUnlockResult result = await UnlockAsync(fixture, WORKBOOK_PASSWORD);

        AssertDecrypted(result, fixture, MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet, WORKBOOK_PASSWORD);
        Assert.Contains(WORKBOOK_CELL, ExtractedText.PackageText(result.DecryptedBytes!));
    }

    [Theory]
    [InlineData(PROTECTED_WORD_DOCUMENT)]
    [InlineData(PROTECTED_WORD_DOCUMENT_AGILE)]
    public async Task AWordDocument_WithItsPassword_IsDecrypted_IntoAPlainPackageThatKeepsItsText(string fixture)
    {
        FileUnlockResult result = await UnlockAsync(fixture, WORD_PASSWORD);

        AssertDecrypted(result, fixture, MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, WORD_PASSWORD);
        Assert.Contains(WORD_PARAGRAPH, ExtractedText.PackageText(result.DecryptedBytes!));
    }

    [Theory]
    [InlineData(PROTECTED_WORKBOOK)]
    [InlineData(PROTECTED_WORKBOOK_AGILE)]
    [InlineData(PROTECTED_WORD_DOCUMENT)]
    [InlineData(PROTECTED_WORD_DOCUMENT_AGILE)]
    public async Task AnOfficeFile_WithAWrongPassword_IsPasswordIncorrect(string fixture)
    {
        AssertLocked(FileProtectionStatus.PasswordIncorrect, await UnlockAsync(fixture, WRONG_PASSWORD));
    }

    [Theory]
    [InlineData(PROTECTED_WORKBOOK)]
    [InlineData(PROTECTED_WORKBOOK_AGILE)]
    [InlineData(PROTECTED_WORD_DOCUMENT)]
    [InlineData(PROTECTED_WORD_DOCUMENT_AGILE)]
    public async Task AnOfficeFile_WithNoPassword_IsPasswordRequired(string fixture)
    {
        AssertLocked(FileProtectionStatus.PasswordRequired, await UnlockAsync(fixture, password: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WORKBOOK_PASSWORD)]
    public async Task AnOfficeFile_UnderRightsManagement_IsUnsupportedProtection_WithOrWithoutAPassword(string? password)
    {
        AssertLocked(FileProtectionStatus.UnsupportedProtection, await UnlockAsync(RIGHTS_MANAGED_PACKAGE, password));
    }

    private async Task<FileUnlockResult> UnlockAsync(string fixture, string? password)
    {
        byte[] bytes = ContractFixture.Bytes(fixture);
        EncryptedContainer? container = EncryptedContainerProbe.Detect(bytes);
        Assert.NotNull(container);

        return await Unlocker.TryUnlockAsync(bytes, container.Value, password, TestContext.Current.CancellationToken);
    }

    private static void AssertDecrypted(FileUnlockResult result, string fixture, MediaType mediaType, string? password)
    {
        Assert.Equal(FileProtectionStatus.Decrypted, result.Status);
        Assert.True(result.WasPasswordProtected);
        Assert.NotNull(result.DecryptedBytes);
        Assert.Null(EncryptedContainerProbe.Detect(result.DecryptedBytes));

        FileInspection inspection = FileContentInspector.Default.Inspect(result.DecryptedBytes, ContractFixture.NameOf(fixture), FileInspectionLimits.Default);
        Assert.Equal(FileInspectionOutcome.Accepted, inspection.Outcome);
        Assert.Equal(mediaType, inspection.MediaType);

        if (password is not null)
        {
            Assert.False(Contains(result.DecryptedBytes, Encoding.ASCII.GetBytes(password)), PASSWORD_IN_COPY);
            Assert.False(Contains(result.DecryptedBytes, Encoding.Unicode.GetBytes(password)), PASSWORD_IN_COPY);
            if (mediaType != MediaType.ApplicationPdf)
            {
                Assert.DoesNotContain(password, ExtractedText.PackageText(result.DecryptedBytes), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task AnOfficeFile_WhoseEncryptionDescriptionNamesAnExternalDtd_NeverFetchesIt()
    {
        using var probe = new LoopbackProbe();
        byte[] tampered = EncryptedPackages.WithEncryptionDescription(Bytes(PROTECTED_WORKBOOK_AGILE),
            xml => EncryptedPackages.WithDocumentType(xml, $"<!ENTITY % external SYSTEM \"{probe.Address.AbsoluteUri}\"> %external;"));

        _ = await Unlocker.TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WORKBOOK_PASSWORD, TestContext.Current.CancellationToken)
            .WaitAsync(EntityPackages.ReadTimeLimit, TestContext.Current.CancellationToken);

        Assert.Equal(0, probe.Connections);
    }

    [Fact]
    public async Task AnOfficeFile_WhoseSpinCountComesFromAnEntity_IsNotDecrypted()
    {
        byte[] tampered = EncryptedPackages.WithEncryptionDescription(Bytes(PROTECTED_WORKBOOK_AGILE),
            xml => EncryptedPackages.WithDocumentType(SpinCount().Replace(xml, "spinCount=\"&spin;\""), "<!ENTITY spin \"100000\">"));

        FileUnlockResult result = await Unlocker.TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WORKBOOK_PASSWORD, TestContext.Current.CancellationToken)
            .WaitAsync(EntityPackages.ReadTimeLimit, TestContext.Current.CancellationToken);

        Assert.NotEqual(FileProtectionStatus.Decrypted, result.Status);
        Assert.Null(result.DecryptedBytes);
    }

    [GeneratedRegex("spinCount=\"\\d+\"")]
    private static partial Regex SpinCount();

    private static void AssertLocked(FileProtectionStatus expected, FileUnlockResult result)
    {
        Assert.Equal(expected, result.Status);
        Assert.Null(result.DecryptedBytes);
        Assert.False(result.WasPasswordProtected);
    }

    private static bool Contains(byte[] bytes, byte[] value)
    {
        return bytes.AsSpan().IndexOf(value) >= 0;
    }
}
