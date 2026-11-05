using System.Collections.Concurrent;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Unlock;
using IniaAi.FileFormats.Word;
using static IniaAi.FileFormats.ContractTests.ContractFixture;

namespace IniaAi.FileFormats.ContractTests.Word;

/// <summary>
/// What DocumentProcessingService and screening rely on from every <see cref="IWordPageReader"/>: every word of a
/// .docx, in order, a long document split into several pages, the same split on every call, the picture on the page
/// of the text before it as an image region the model can read, and a failure naming a document that still needs its
/// password. Where one page ends is the reader's own choice, so no case checks a page boundary.
/// </summary>
public abstract class WordPageReaderContract
{
    private const string PAGE_TEXT_PREFIX = "Page ";
    private const int TEN_PAGES_PARAGRAPHS = 10;
    private const int PICTURE_DOCUMENT_PARAGRAPHS = 2;
    private const int LONG_DOCUMENT_PARAGRAPHS = 150;
    private const int LEADING_PAGES = 1;

    // A layout reader takes about a second over the long document, so the cases of one reader share one read of it.
    private static readonly ConcurrentDictionary<Type, Lazy<IReadOnlyList<WordPageContent>>> _longDocumentPages = new();

    protected abstract IWordPageReader Reader { get; }

    [Fact]
    public void ExtractPages_KeepsEveryWordInOrder_WithNoImageRegionOnAPageWithoutImages()
    {
        IReadOnlyList<WordPageContent> pages = Reader.ExtractPages(Bytes(TEN_PAGES_WORD_DOCUMENT), NameOf(TEN_PAGES_WORD_DOCUMENT), VisionTier.Standard);

        Assert.Equal(WordsOf(PageTexts(TEN_PAGES_PARAGRAPHS)), WordsOf(pages.Select(page => page.Text)));
        Assert.All(pages, page => Assert.Empty(page.ImageRegions));
    }

    [Fact]
    public void ExtractPages_OfALongDocument_KeepsEveryWordInOrder_OnSeveralPages()
    {
        IReadOnlyList<WordPageContent> pages = LongDocumentPages();

        Assert.Equal(WordsOf(LongDocumentParagraphs()), WordsOf(pages.Select(page => page.Text)));
        Assert.True(pages.Count > 1, $"The reader put the {LONG_DOCUMENT_PARAGRAPHS} paragraphs on {pages.Count} page.");
    }

    [Fact]
    public void ExtractPages_SplitsTheSameBytesTheSameWay_OnEveryCall()
    {
        IReadOnlyList<WordPageContent> again = Reader.ExtractPages(Bytes(LONG_WORD_DOCUMENT), NameOf(LONG_WORD_DOCUMENT), VisionTier.Standard);

        Assert.Equal(LongDocumentPages().Select(page => page.Text), again.Select(page => page.Text));
    }

    [Fact]
    public void ExtractPages_HandsThePictureToTheModel_AsOneImageRegionOnThePageOfTheTextBeforeIt_ThatFitsItsVisionTier()
    {
        IReadOnlyList<WordPageContent> pages = Reader.ExtractPages(Bytes(PICTURE_WORD_DOCUMENT), NameOf(PICTURE_WORD_DOCUMENT), VisionTier.Standard);

        Assert.Equal(WordsOf(PageTexts(PICTURE_DOCUMENT_PARAGRAPHS)), WordsOf(pages.Select(page => page.Text)));
        WordPageContent pageWithPicture = Assert.Single(pages, page => page.ImageRegions.Count > 0);
        Assert.Contains(PAGE_TEXT_PREFIX + 1, pageWithPicture.Text, StringComparison.Ordinal);
        BinaryContent region = Assert.Single(pageWithPicture.ImageRegions);
        Assert.Contains(region.MediaType, new[] { MediaType.ImagePng, MediaType.ImageJpeg });
        PictureSize? size = Pictures.ReadSize(region.BinaryData.ToArray());
        Assert.NotNull(size);
        Assert.InRange(Math.Max(size.Width, size.Height), 1, VisionTier.STANDARD_LONG_EDGE_PX);
    }

    [Theory]
    [InlineData(LEADING_PAGES)]
    [InlineData(LONG_DOCUMENT_PARAGRAPHS * 2)]
    public void ExtractPageTexts_ReadsTheLeadingPagesItIsAskedFor_AsExtractPagesSplitsThem(int maxPages)
    {
        IReadOnlyList<string> texts = Reader.ExtractPageTexts(Bytes(LONG_WORD_DOCUMENT), NameOf(LONG_WORD_DOCUMENT), maxPages);

        Assert.Equal(LongDocumentPages().Take(maxPages).Select(page => page.Text.Trim()), texts.Select(text => text.Trim()));
    }

    [Theory]
    [InlineData(PROTECTED_WORD_DOCUMENT)]
    [InlineData(PROTECTED_WORD_DOCUMENT_AGILE)]
    public void ExtractPages_OfADocumentThatStillNeedsItsPassword_FailsNamingTheDocument(string fixture)
    {
        IWordPageReader reader = Reader;
        string name = NameOf(fixture);

        DocumentPasswordProtectedException failure = Assert.Throws<DocumentPasswordProtectedException>(
            () => reader.ExtractPages(Bytes(fixture), name, VisionTier.Standard));

        Assert.Equal(name, failure.DocumentName);
    }

    [Theory]
    [InlineData(PROTECTED_WORD_DOCUMENT)]
    [InlineData(PROTECTED_WORD_DOCUMENT_AGILE)]
    public void ExtractPageTexts_OfADocumentThatStillNeedsItsPassword_FailsNamingTheDocument(string fixture)
    {
        IWordPageReader reader = Reader;
        string name = NameOf(fixture);

        DocumentPasswordProtectedException failure = Assert.Throws<DocumentPasswordProtectedException>(
            () => reader.ExtractPageTexts(Bytes(fixture), name, LEADING_PAGES));

        Assert.Equal(name, failure.DocumentName);
    }

    private static string WordsOf(IEnumerable<string> texts)
    {
        return ExtractedText.Words(string.Join(' ', texts));
    }

    private static IEnumerable<string> PageTexts(int count)
    {
        return Enumerable.Range(1, count).Select(pageNumber => PAGE_TEXT_PREFIX + pageNumber);
    }

    private static IEnumerable<string> LongDocumentParagraphs()
    {
        return Enumerable.Range(1, LONG_DOCUMENT_PARAGRAPHS)
            .Select(number => $"Paragraph {number} says the covenant test for quarter {number} stays inside its limit and the margin holds.");
    }

    private IReadOnlyList<WordPageContent> LongDocumentPages()
    {
        IWordPageReader reader = Reader;
        return _longDocumentPages.GetOrAdd(
            GetType(),
            _ => new Lazy<IReadOnlyList<WordPageContent>>(() => reader.ExtractPages(Bytes(LONG_WORD_DOCUMENT), NameOf(LONG_WORD_DOCUMENT), VisionTier.Standard))).Value;
    }
}
