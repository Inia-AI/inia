using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Tests.Data.Documents;

public class DocumentFileNameTests
{
    [Theory]
    [InlineData("Report", ".pdf", "Report.pdf")]
    [InlineData("Report.pdf", ".pdf", "Report.pdf")]
    [InlineData("Report.PDF", ".pdf", "Report.PDF")]
    [InlineData("Report   ", ".pdf", "Report.pdf")]
    [InlineData("Report.pdf  ", ".pdf", "Report.pdf")]
    [InlineData("Report", "", "Report")]
    public void FileName_ReattachesTheExtensionWithoutTrailingWhitespace(string name, string extension, string expected)
    {
        Document document = new()
        {
            Name = name,
            FileMetadata = new FileMetadata { Extension = extension },
        };

        Assert.Equal(expected, document.FileName);
    }
}
