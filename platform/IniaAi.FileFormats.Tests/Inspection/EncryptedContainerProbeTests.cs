using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Inspection;

namespace IniaAi.FileFormats.Tests.Inspection;

public class EncryptedContainerProbeTests
{
    [Fact]
    public void IsRightsManagedPackage_PackageWithNoEncryptionInfo_IsTrue()
    {
        Assert.True(EncryptedContainerProbe.IsRightsManagedPackage(ContractFixture.Bytes(ContractFixture.RIGHTS_MANAGED_PACKAGE)));
    }

    [Theory]
    [InlineData(ContractFixture.PROTECTED_WORKBOOK)]
    [InlineData(ContractFixture.PROTECTED_WORD_DOCUMENT)]
    [InlineData(ContractFixture.PROTECTED_PDF)]
    [InlineData(ContractFixture.CELL_TEXTS_WORKBOOK)]
    [InlineData(ContractFixture.OUTLOOK_MESSAGE)]
    public void IsRightsManagedPackage_PasswordProtectedOrPlainFile_IsFalse(string fixture)
    {
        Assert.False(EncryptedContainerProbe.IsRightsManagedPackage(ContractFixture.Bytes(fixture)));
    }

    [Fact]
    public void IsRightsManagedPackage_BytesThatAreNoFile_IsFalse()
    {
        Assert.False(EncryptedContainerProbe.IsRightsManagedPackage("not a compound file"u8.ToArray()));
    }
}
