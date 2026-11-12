using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;

namespace IniaAi.FileFormats.Tests.Unlock;

/// <summary>A crafted encryption header cannot make the open unlocker hash or allocate without limit.</summary>
public partial class OfficePackageDecryptionTests
{
    private const string WORKBOOK_PASSWORD = "contract-workbook-pw";
    private const string OVER_THE_SPEC_LIMIT = "10000001";
    private const string WRONG_PASSWORD = "not-the-password";
    private const int DIRECTORY_ENTRY_BYTES = 128;
    private const int DIRECTORY_ENTRY_SIZE_OFFSET = 120;

    [GeneratedRegex("spinCount=\"\\d+\"")]
    private static partial Regex SpinCount();

    [GeneratedRegex("(<p:encryptedKey [^>]*?blockSize=\")\\d+")]
    private static partial Regex PasswordKeyBlockSize();

    [GeneratedRegex("(<keyData [^>]*?blockSize=\")\\d+")]
    private static partial Regex KeyDataBlockSize();

    [Fact]
    public async Task AFile_WhoseStreamClaimsMoreBytesThanTheFileHolds_HasUnsupportedProtection_WithoutAPassword()
    {
        byte[] file = ContractFixture.Bytes(ContractFixture.PROTECTED_WORKBOOK_AGILE);
        int entry = DirectoryEntry(file, EncryptedPackages.ENCRYPTION_INFO);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(entry + DIRECTORY_ENTRY_SIZE_OFFSET), int.MaxValue);

        FileUnlockResult result = await new OpenDocumentUnlocker().TryUnlockAsync(file, EncryptedContainer.OfficePackage, password: null, TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    [Fact]
    public async Task AFile_WhosePasswordKeyClaimsAHugeBlock_HasUnsupportedProtection_ForAWrongPassword()
    {
        byte[] tampered = WithEncryptionDescription(xml => PasswordKeyBlockSize().Replace(xml, $"${{1}}{int.MaxValue}"));

        FileUnlockResult result = await new OpenDocumentUnlocker().TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WRONG_PASSWORD, TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    [Fact]
    public async Task AFile_WhoseDataKeyClaimsAnEmptyBlock_HasUnsupportedProtection()
    {
        byte[] tampered = WithEncryptionDescription(xml => KeyDataBlockSize().Replace(xml, "${1}0"));

        FileUnlockResult result = await new OpenDocumentUnlocker().TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WORKBOOK_PASSWORD, TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    [Fact]
    public async Task AFile_WhoseSpinCountIsOverTheSpecLimit_HasUnsupportedProtection()
    {
        byte[] tampered = WithEncryptionDescription(xml => SpinCount().Replace(xml, $"spinCount=\"{OVER_THE_SPEC_LIMIT}\""));

        FileUnlockResult result = await new OpenDocumentUnlocker().TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WORKBOOK_PASSWORD, TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    [Fact]
    public async Task AFile_WhosePackageClaimsMoreBytesThanItHolds_HasUnsupportedProtection()
    {
        byte[] tampered = EncryptedPackages.Rebuilt(ContractFixture.Bytes(ContractFixture.PROTECTED_WORKBOOK_AGILE), EncryptedPackages.ENCRYPTED_PACKAGE, package =>
        {
            byte[] claimed = [.. package];
            BinaryPrimitives.WriteInt64LittleEndian(claimed, long.MaxValue / 2);
            return claimed;
        });

        FileUnlockResult result = await new OpenDocumentUnlocker().TryUnlockAsync(tampered, EncryptedContainer.OfficePackage, WORKBOOK_PASSWORD, TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    private static byte[] WithEncryptionDescription(Func<string, string> change)
    {
        return EncryptedPackages.WithEncryptionDescription(ContractFixture.Bytes(ContractFixture.PROTECTED_WORKBOOK_AGILE), change);
    }

    /// <summary>The offset of the directory entry named <paramref name="name"/>; entries start on 128-byte boundaries.</summary>
    private static int DirectoryEntry(byte[] file, string name)
    {
        byte[] utf16Name = Encoding.Unicode.GetBytes(name);
        for (int offset = 0; offset + DIRECTORY_ENTRY_BYTES <= file.Length; offset += DIRECTORY_ENTRY_BYTES)
        {
            if (file.AsSpan(offset).StartsWith(utf16Name))
            {
                return offset;
            }
        }

        throw new InvalidDataException($"The compound file has no directory entry named {name}.");
    }
}
