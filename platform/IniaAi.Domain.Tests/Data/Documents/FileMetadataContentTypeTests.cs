using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Tests.Data.Documents;

/// <summary>
/// The content type a stored file is served with is the type its bytes were detected as, when
/// the document is loaded; the extension is only the fallback for metadata read on its own.
/// </summary>
public class FileMetadataContentTypeTests
{
    [Fact]
    public void ContentType_IsTheDocumentsDetectedType_WhenTheDocumentIsLoaded()
    {
        var metadata = new FileMetadata("org-1", "statement.exe", [1, 2, 3]);
        metadata.Document = new Document(metadata, MediaType.ApplicationPdf);

        Assert.Equal("application/pdf", metadata.ContentType);
    }

    [Fact]
    public void ContentType_FallsBackToTheExtension_WhenNoDocumentIsLoaded()
    {
        var metadata = new FileMetadata("org-1", "report.pdf", [1, 2, 3]);

        Assert.Equal("application/pdf", metadata.ContentType);
    }
}
