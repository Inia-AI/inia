using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Documents;

public class FileMetadataNameTests
{
    private const string NAME_WITH_A_LINE_BREAK = "evil\r\nFAKE LOG LINE.pdf";
    private const string NAME_ON_ONE_LINE = "evil  FAKE LOG LINE";

    [Fact]
    public void ANameWithALineBreak_IsStoredOnOneLine()
    {
        var fileMetadata = new FileMetadata("org", NAME_WITH_A_LINE_BREAK, 128, StorageProvider.Database);

        Assert.Equal(NAME_ON_ONE_LINE, fileMetadata.Name);
        Assert.Equal(".pdf", fileMetadata.Extension);
    }

    [Fact]
    public void ANameWithALineBreak_IsStoredOnOneLine_WhenTheBytesArriveWithIt()
    {
        var fileMetadata = new FileMetadata("org", NAME_WITH_A_LINE_BREAK, [1, 2, 3]);

        Assert.Equal(NAME_ON_ONE_LINE, fileMetadata.Name);
    }

    [Fact]
    public void ANameWithoutControlCharacters_IsStoredAsItArrived()
    {
        var fileMetadata = new FileMetadata("org", "Annual  Report 2025.pdf", 128, StorageProvider.Database);

        Assert.Equal("Annual  Report 2025", fileMetadata.Name);
    }
}
