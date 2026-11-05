using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.Documents;

public class PageTests
{
    [Theory]
    [InlineData(PageStatus.OCRNeedsReview, true)]
    [InlineData(PageStatus.OCRSuccessful, false)]
    [InlineData(PageStatus.OCRFailed, false)]
    [InlineData(PageStatus.ProcessedSuccessfully, false)]
    [InlineData(PageStatus.NotProcessed, false)]
    public void IsOcrImageOnly_TrueOnlyForOcrNeedsReview(PageStatus status, bool expected)
    {
        Page page = CreatePage();
        page.Status = status;

        Assert.Equal(expected, page.IsOcrImageOnly);
    }

    [Fact]
    public void OcrNeedsReview_IsAppendedAtEndPreservingIntBacking()
    {
        Assert.Equal(7, (int)PageStatus.OCRNeedsReview);
    }

    [Fact]
    public void ReadableText_ReturnsOverview_WhenImageOnly()
    {
        Page page = CreatePage();
        page.Overview = "image-derived overview";
        page.Status = PageStatus.OCRNeedsReview;

        Assert.Equal("image-derived overview", page.ReadableText);
    }

    [Fact]
    public void ReadableText_ReturnsText_WhenNotImageOnly()
    {
        Page page = CreatePage();
        page.Text = "literal text";
        page.Overview = "overview";

        Assert.Equal("literal text", page.ReadableText);
    }

    [Fact]
    public void Text_RemovesNullCharacters()
    {
        Page page = CreatePage();

        page.Text = "scraped\0 text\0";

        Assert.Equal("scraped text", page.Text);
    }

    [Fact]
    public void RawText_RemovesNullCharacters()
    {
        Page page = CreatePage();

        page.RawText = "raw\0 text";

        Assert.Equal("raw text", page.RawText);
    }

    [Fact]
    public void SourceUrl_RemovesNullCharacters()
    {
        Page page = CreatePage();

        page.SourceUrl = "https://example.com/article\0";

        Assert.Equal("https://example.com/article", page.SourceUrl);
    }

    [Fact]
    public void SourceUrl_NullAndCleanValues_PassThroughUnchanged()
    {
        Page page = CreatePage();

        page.SourceUrl = null;
        Assert.Null(page.SourceUrl);

        page.SourceUrl = "https://example.com/article";
        Assert.Equal("https://example.com/article", page.SourceUrl);
    }

    private static Page CreatePage()
    {
        var document = new Document(new FileMetadata("", "test.txt", []), MediaType.TextPlain);
        return new Page(document, pageNumber: 1, filePageNumber: 1);
    }
}
