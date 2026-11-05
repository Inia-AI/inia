using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;
using IniaAi.Testing.Documents;

namespace IniaAi.FileFormats.Tests.Unlock;

/// <summary>The encrypted files that the test fixtures write open with their password, so a test of a locked file tests the lock.</summary>
public class EncryptedFixtureTests
{
    private const string PASSWORD = "fixture-pw-1";

    [Fact]
    public async Task AnEncryptedWorkbookFixture_DecryptsWithItsPassword_IntoTheWorkbook()
    {
        byte[] plain = OoxmlFixtures.Workbook();

        FileUnlockResult result = await Unlock(OfficeEncryption.Encrypted(plain, PASSWORD), EncryptedContainer.OfficePackage, PASSWORD);

        Assert.Equal(FileProtectionStatus.Decrypted, result.Status);
        Assert.Equal(plain, result.DecryptedBytes);
        Assert.Equal(FileProtectionStatus.Decrypted, (await Unlock(OoxmlFixtures.Workbook(PASSWORD), EncryptedContainer.OfficePackage, PASSWORD)).Status);
    }

    [Fact]
    public async Task AnEncryptedPdfFixture_DecryptsWithItsUserPassword_AndRefusesAWrongOne()
    {
        byte[] encrypted = PdfFixtures.Encrypted(PdfFixtures.Plain("Locked page text"), PASSWORD, "owner-pw-1");

        Assert.Equal(FileProtectionStatus.PasswordIncorrect, (await Unlock(encrypted, EncryptedContainer.Pdf, "wrong")).Status);
        FileUnlockResult result = await Unlock(encrypted, EncryptedContainer.Pdf, PASSWORD);
        Assert.Equal(FileProtectionStatus.Decrypted, result.Status);
        using var pdf = FileFormatsUnderTest.Pdf.Open(new MemoryStream(result.DecryptedBytes!), "unlocked.pdf");
        Assert.Contains("Locked page text", pdf.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOwnerOnlyPdfFixture_OpensWithoutAPassword()
    {
        byte[] restricted = PdfFixtures.Encrypted(PdfFixtures.Plain("Restricted page text"), userPassword: string.Empty, "owner-pw-1");

        FileUnlockResult result = await Unlock(restricted, EncryptedContainer.Pdf, password: null);

        Assert.Equal(FileProtectionStatus.Decrypted, result.Status);
    }

    private static Task<FileUnlockResult> Unlock(byte[] bytes, EncryptedContainer container, string? password)
    {
        return new OpenDocumentUnlocker().TryUnlockAsync(bytes, container, password, TestContext.Current.CancellationToken);
    }
}
