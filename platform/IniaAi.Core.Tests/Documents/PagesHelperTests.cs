using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.Images;
using IniaAi.Testing;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SkiaSharp;
using Document = IniaAi.Domain.Entities.Documents.Document;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;
using Page = IniaAi.Domain.Entities.Documents.Page;

namespace IniaAi.Core.Tests.Documents;

public class PagesHelperTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ILogger _logger;

    public PagesHelperTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _logger = TestsLogger.CreateLogger(testOutputHelper);
    }

    [Fact]
    public async Task EnsurePageImageAsync_PdfPage_RendersImageFromStorage()
    {
        byte[] pdfBytes = CreateSamplePdf();
        var fileMetadata = new FileMetadata("org1", "test.pdf", pdfBytes.Length, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();
        _ = storage.RetrieveFileBytesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(pdfBytes);

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.NotNull(page.Image);
        Assert.Equal(MediaType.ImagePng, page.Image.MediaType);
        Assert.NotEmpty(page.Image.BinaryData.ToArray());
    }

    [Fact]
    public async Task EnsurePageImageAsync_ImageDocument_NormalizesToModelSupportedPng()
    {
        byte[] imageBytes = CreateImage(200, 150, ImageEncoding.Jpeg);
        var fileMetadata = new FileMetadata("org1", "scan.jpg", imageBytes.Length, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ImageJpeg);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();
        _ = storage.RetrieveFileBytesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(imageBytes);

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.NotNull(page.Image);
        Assert.Equal(MediaType.ImagePng, page.Image.MediaType);
        Assert.Equal(MediaType.ImagePng, page.ImageMediaType);
        AssertIsPng(page.Image.BinaryData.ToArray());
    }

    [Fact]
    public async Task EnsurePageImageAsync_TiffImageDocument_GetsNoPagePicture()
    {
        byte[] tiffBytes = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00];
        var fileMetadata = new FileMetadata("org1", "scan.tiff", tiffBytes.Length, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ImageTiff);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();
        _ = storage.RetrieveFileBytesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(tiffBytes);

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.Null(page.Image);
    }

    [Fact]
    public async Task EnsurePageImageAsync_OversizeImage_IsResizedUnderCap()
    {
        byte[] imageBytes = CreateImage(4000, 3000, ImageEncoding.Png);
        var fileMetadata = new FileMetadata("org1", "big.png", imageBytes.Length, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ImagePng);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();
        _ = storage.RetrieveFileBytesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(imageBytes);

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.NotNull(page.Image);
        PictureSize resized = SizeOf(page.Image);
        Assert.True(Math.Max(resized.Width, resized.Height) <= 1568);
    }

    [Fact]
    public async Task EnsurePageImageAsync_PdfPage_PrefersLoadedBytesAndSkipsStorage()
    {
        byte[] pdfBytes = CreateSamplePdf();
        var fileMetadata = new FileMetadata("org1", "test.pdf", pdfBytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.NotNull(page.Image);
        Assert.Equal(MediaType.ImagePng, page.Image.MediaType);
        _ = storage.DidNotReceive().RetrieveFileBytesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task EnsurePageImageAsync_ImageAlreadyPresent_IsIdempotentAndSkipsStorage()
    {
        var fileMetadata = new FileMetadata("org1", "test.pdf", 3, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var existing = new BinaryContent([1, 2, 3], MediaType.ImagePng);
        var page = new Page(document, 1, 1) { Image = existing };
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.Same(existing, page.Image);
        _ = storage.DidNotReceive().RetrieveFileBytesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void GenerateImagesFromPdfPages_ShouldGenerateImagesForPdfPagesAsync()
    {
        // Arrange
        byte[] pdfBytes = CreateSamplePdf();
        var fileMetadata = new FileMetadata("org1", "test.pdf", pdfBytes.Length, StorageProvider.Database, Guid.NewGuid().ToString());
        fileMetadata.LoadBytes(pdfBytes); // Load bytes into FileMetadata
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page1 = new Page(document, 1, 1);
        var page2 = new Page(document, 2, 2);
        var pages = new List<Page> { page1, page2 };

        // Act
        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        // Assert
        foreach (Page page in pages)
        {
            Assert.NotNull(page.Image);
            Assert.Equal(MediaType.ImagePng, page.Image.MediaType);
            Assert.NotEmpty(page.Image.BinaryData.ToArray());
            _testOutputHelper.WriteLine($"Generated image for page {page.PageNumber} with {page.Image.BinaryData.ToArray().Length} bytes");
        }
    }

    [Fact]
    public async Task GenerateImagesFromPdfPages_BlocksAskingForTheSamePagesAtOnce_RenderEachPageOnce()
    {
        const int PARALLEL_BLOCKS = 4;
        byte[] pdfBytes = CreateSamplePdf();
        var document = new Document(new FileMetadata("org1", "shared.pdf", pdfBytes), MediaType.ApplicationPdf);
        List<Page> pages = [new Page(document, 1, 1), new Page(document, 2, 2)];
        var logger = new CapturingLogger<PagesHelper>();
        using var start = new Barrier(PARALLEL_BLOCKS);

        Task[] blocks = [.. Enumerable.Range(0, PARALLEL_BLOCKS).Select(_ => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, logger, pages);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default))];
        await Task.WhenAll(blocks);

        Assert.All(pages, page => Assert.NotNull(page.Image));
        Assert.Equal(pages.Count, logger.Entries.Count(entry => entry.Message.StartsWith("Converted page", StringComparison.Ordinal)));
    }

    [Fact]
    public void GenerateImagesFromPdfPages_PageWiderThanModelBudget_RendersWithinModelImageBudget()
    {
        byte[] pdfBytes = PdfFixtures.SizedPage(2592, 842);
        var fileMetadata = new FileMetadata("org1", "wide.pdf", pdfBytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1);
        var pages = new List<Page> { page };

        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        AssertRenderedWithinModelBudget(page);
    }

    [Fact]
    public void GenerateImagesFromPdfPages_BothHalvesOfOneScannedPage_RenderAtHalfTheWidthEach()
    {
        byte[] pdfBytes = PdfFixtures.SizedPage(1190, 842);
        var document = new Document(new FileMetadata("org1", "facing.pdf", pdfBytes), MediaType.ApplicationPdf);
        var left = new Page(document, 1, 1) { Location = PageLocation.LeftSide };
        var right = new Page(document, 2, 1) { Location = PageLocation.RightSide };

        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, [left, right]);

        Assert.NotNull(left.Image);
        Assert.NotNull(right.Image);
        Assert.Equal(SizeOf(left.Image), SizeOf(right.Image));
        Assert.InRange(SizeOf(right.Image).Width * 2, SizeOf(right.Image).Height * 1190 / 842 - 2, SizeOf(right.Image).Height * 1190 / 842 + 2);
    }

    [Fact]
    public void GenerateImagesFromPdfPages_NormalPage_KeepsDefaultRenderResolution()
    {
        byte[] pdfBytes = PdfFixtures.SizedPage(595, 842);
        var fileMetadata = new FileMetadata("org1", "a4.pdf", pdfBytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1);
        var pages = new List<Page> { page };

        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        Assert.NotNull(page.Image);
        PictureSize rendered = SizeOf(page.Image);
        Assert.InRange(rendered.Height, 1166, 1172);
    }

    [Fact]
    public async Task EnsurePageImageAsync_PdfPageWiderThanModelBudget_RendersWithinModelImageBudget()
    {
        byte[] pdfBytes = PdfFixtures.SizedPage(2592, 842);
        var fileMetadata = new FileMetadata("org1", "wide.pdf", pdfBytes.Length, StorageProvider.Database, "storage-key");
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1);
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();
        _ = storage.RetrieveFileBytesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(pdfBytes);

        await PagesHelper.EnsurePageImageAsync(page, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        AssertRenderedWithinModelBudget(page);
    }

    [Fact]
    public void GenerateImagesFromPdfPages_ShouldSkipPagesWithExistingImagesAsync()
    {
        // Arrange
        byte[] pdfBytes = CreateSamplePdf();
        var fileMetadata = new FileMetadata("org1", "test.pdf", pdfBytes.Length, StorageProvider.Database, Guid.NewGuid().ToString());
        fileMetadata.LoadBytes(pdfBytes); // Load bytes into FileMetadata
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page1 = new Page(document, 1, 1) { Image = new BinaryContent([1, 2, 3], MediaType.ImagePng) };
        var page2 = new Page(document, 2, 2);
        var pages = new List<Page> { page1, page2 };
        byte[] originalImageBytes = page1.Image.BinaryData.ToArray();

        // Act
        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        // Assert
        byte[] array = page1.Image.BinaryData.ToArray();
        Assert.Equal(originalImageBytes, array);
        Assert.NotNull(page2.Image);
        Assert.Equal(MediaType.ImagePng, page2.Image.MediaType);
        Assert.NotEmpty(page2.Image.BinaryData.ToArray());
    }

    [Fact]
    public void GenerateImagesFromPdfPages_ShouldHandleNonPdfDocumentsAsync()
    {
        // Arrange
        var file = new FileMetadata("org1", "test.txt", "ABC"u8.ToArray()); // ABC
        var document = new Document(file, MediaType.TextPlain);
        var page = new Page(document, 1, 1);
        var pages = new List<Page> { page };

        // Act
        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        // Assert
        Assert.Null(page.Image);
    }

    [Fact]
    public void GenerateImagesFromPdfPages_ShouldHandleMixedDocumentTypesAsync()
    {
        // Arrange
        byte[] pdfBytes = CreateSamplePdf();
        var pdfFile = new FileMetadata("org1", "test.pdf", pdfBytes);
        var pdfDocument = new Document(pdfFile, MediaType.ApplicationPdf);
        var txtFile = new FileMetadata("org1", "test.txt", "ABC"u8.ToArray());
        var txtDocument = new Document(txtFile, MediaType.TextPlain);
        var pdfPage = new Page(pdfDocument, 1, 1);
        var txtPage = new Page(txtDocument, 1, 1);
        var pages = new List<Page> { pdfPage, txtPage };

        // Act
        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        // Assert
        Assert.NotNull(pdfPage.Image);
        Assert.Equal(MediaType.ImagePng, pdfPage.Image.MediaType);
        Assert.NotEmpty(pdfPage.Image.BinaryData.ToArray());

        Assert.Null(txtPage.Image);
    }

    [Fact]
    public void GenerateImagesFromPdfPages_ImageDocumentWithLoadedBytes_ProducesPng()
    {
        byte[] imageBytes = CreateImage(200, 150, ImageEncoding.Jpeg);
        var fileMetadata = new FileMetadata("org1", "scan.jpg", imageBytes);
        var document = new Document(fileMetadata, MediaType.ImageJpeg);
        var page = new Page(document, 1, 1) { Status = PageStatus.OCRNeedsReview };
        var pages = new List<Page> { page };

        PagesHelper.GenerateImagesFromPdfPages(FileFormatsUnderTest.Pdf, _logger, pages);

        Assert.NotNull(page.Image);
        Assert.Equal(MediaType.ImagePng, page.Image.MediaType);
        AssertIsPng(page.Image.BinaryData.ToArray());
    }

    [Fact]
    public async Task EnsureImageOnlyPageImagesAsync_WithLoadedBytes_ProducesImagesWithoutStorage()
    {
        byte[] pdfBytes = CreateSamplePdf();
        var fileMetadata = new FileMetadata("org1", "test.pdf", pdfBytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var page1 = new Page(document, 1, 1) { Status = PageStatus.OCRNeedsReview };
        var page2 = new Page(document, 2, 2) { Status = PageStatus.OCRNeedsReview };
        var pages = new List<Page> { page1, page2 };
        IFileStorageManager storage = Substitute.For<IFileStorageManager>();

        await PagesHelper.EnsureImageOnlyPageImagesAsync(pages, FileFormatsUnderTest.Pdf, storage, _logger, TestContext.Current.CancellationToken);

        Assert.NotNull(page1.Image);
        Assert.NotNull(page2.Image);
        Assert.Equal(MediaType.ImagePng, page1.Image.MediaType);
        _ = storage.DidNotReceive().RetrieveFileBytesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    private static byte[] CreateSamplePdf()
    {
        return PdfFixtures.Pages("Page 1 content", "Page 2 content");
    }

    private static void AssertRenderedWithinModelBudget(Page page)
    {
        Assert.NotNull(page.Image);
        PictureSize rendered = SizeOf(page.Image);
        Assert.True(
            Math.Max(rendered.Width, rendered.Height) <= 1568,
            $"Rendered image is {rendered.Width}x{rendered.Height}px; the long edge must stay within the model image budget of 1568px.");
    }

    private enum ImageEncoding
    {
        Jpeg,
        Png
    }

    private static byte[] CreateImage(int width, int height, ImageEncoding encoding)
    {
        using var image = new SKBitmap(width, height);
        image.Erase(SKColors.Blue);

        SKEncodedImageFormat format = encoding is ImageEncoding.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using SKData encoded = image.Encode(format, 90);

        return encoded.ToArray();
    }

    private static PictureSize SizeOf(BinaryContent picture)
    {
        PictureSize? size = Pictures.ReadSize(picture.BinaryData.ToArray());
        Assert.NotNull(size);
        return size;
    }

    private static void AssertIsPng(byte[] bytes)
    {
        using SKCodec codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithDocuments_ReturnsOrderedPages()
    {
        // Arrange
        var fileMetadata1 = new FileMetadata("1234", "Document_B.pdf", 100, StorageProvider.Database, "file1");
        var fileMetadata2 = new FileMetadata("5678", "Document_A.pdf", 100, StorageProvider.Database, "file2");
        var fileMetadata3 = new FileMetadata("9012", "Document_C.pdf", 100, StorageProvider.Database, "file3");

        var document1 = new Document(fileMetadata1, MediaType.ApplicationPdf);
        var document2 = new Document(fileMetadata2, MediaType.ApplicationPdf);
        var document3 = new Document(fileMetadata3, MediaType.ApplicationPdf);

        _ = new Page(document1, 2, 2) { Text = "Document B Page 2" };
        _ = new Page(document1, 1, 1) { Text = "Document B Page 1" };
        _ = new Page(document2, 3, 3) { Text = "Document A Page 3" };
        _ = new Page(document2, 1, 1) { Text = "Document A Page 1" };
        _ = new Page(document3, 1, 1) { Text = "Document C Page 1" };

        var documents = new List<Document> { document1, document2, document3 };

        // Act
        List<Page> result = PagesHelper.GetOrderedPages(documents);

        // Assert
        Assert.Equal(5, result.Count);

        Assert.Equal("Document_A", result[0].Document?.Name);
        Assert.Equal(1, result[0].PageNumber);
        Assert.Equal("Document A Page 1", result[0].Text);

        Assert.Equal("Document_A", result[1].Document?.Name);
        Assert.Equal(3, result[1].PageNumber);
        Assert.Equal("Document A Page 3", result[1].Text);

        Assert.Equal("Document_B", result[2].Document?.Name);
        Assert.Equal(1, result[2].PageNumber);
        Assert.Equal("Document B Page 1", result[2].Text);

        Assert.Equal("Document_B", result[3].Document?.Name);
        Assert.Equal(2, result[3].PageNumber);
        Assert.Equal("Document B Page 2", result[3].Text);

        Assert.Equal("Document_C", result[4].Document?.Name);
        Assert.Equal(1, result[4].PageNumber);
        Assert.Equal("Document C Page 1", result[4].Text);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithEmptyDocuments_ReturnsEmptyList()
    {
        // Arrange
        var documents = new List<Document>();

        // Act
        List<Page> result = PagesHelper.GetOrderedPages(documents);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithDocumentsWithNoPages_ReturnsEmptyList()
    {
        // Arrange
        var fileMetadata1 = new FileMetadata("1234", "Document1.pdf", 100, StorageProvider.Database, "file1");
        var fileMetadata2 = new FileMetadata("5678", "Document2.pdf", 100, StorageProvider.Database, "file2");
        var document1 = new Document(fileMetadata1, MediaType.ApplicationPdf);
        var document2 = new Document(fileMetadata2, MediaType.ApplicationPdf);
        var documents = new List<Document> { document1, document2 };

        // Act
        List<Page> result = PagesHelper.GetOrderedPages(documents);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithSingleDocument_ReturnsPagesOrderedByPageNumber()
    {
        // Arrange
        var fileMetadata = new FileMetadata("1234", "SingleDocument.pdf", 100, StorageProvider.Database, "file1");
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);

        _ = new Page(document, 3, 3) { Text = "Page 3" };
        _ = new Page(document, 1, 1) { Text = "Page 1" };
        _ = new Page(document, 2, 2) { Text = "Page 2" };
        var documents = new List<Document> { document };

        // Act
        List<Page> result = PagesHelper.GetOrderedPages(documents);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].PageNumber);
        Assert.Equal("Page 1", result[0].Text);
        Assert.Equal(2, result[1].PageNumber);
        Assert.Equal("Page 2", result[1].Text);
        Assert.Equal(3, result[2].PageNumber);
        Assert.Equal("Page 3", result[2].Text);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithDocumentsHavingSameName_OrdersCorrectly()
    {
        // Arrange
        var fileMetadata1 = new FileMetadata("1234", "SameName.pdf", 100, StorageProvider.Database, "file1");
        var fileMetadata2 = new FileMetadata("5678", "SameName.pdf", 100, StorageProvider.Database, "file2");
        var document1 = new Document(fileMetadata1, MediaType.ApplicationPdf);
        var document2 = new Document(fileMetadata2, MediaType.ApplicationPdf);

        var page1_2 = new Page(document1, 2, 2) { Text = "Doc1 Page 2" };
        var page1_1 = new Page(document1, 1, 1) { Text = "Doc1 Page 1" };
        var page2_1 = new Page(document2, 1, 1) { Text = "Doc2 Page 1" };

        var documents = new List<Document> { document1, document2 };

        // Act
        List<Page> result = PagesHelper.GetOrderedPages(documents);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, page => Assert.Equal("SameName", page.Document?.Name));

        var pageNumbers = result.Select(p => p.PageNumber).ToList();
        var texts = result.Select(p => p.Text).ToList();

        Assert.True(pageNumbers.SequenceEqual([1, 1, 2]) || pageNumbers.SequenceEqual([1, 2, 1]));
        Assert.Contains("Doc1 Page 1", texts);
        Assert.Contains("Doc1 Page 2", texts);
        Assert.Contains("Doc2 Page 1", texts);
    }

    [Fact]
    public void GetOrderedPages_WhenCalledWithNullDocumentCollection_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<Document> documents = null!;

        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() => PagesHelper.GetOrderedPages(documents));
    }
    [Fact]
    public void GetTextsInputContext_WhenCalledWithPages_ReturnsTextWithCorrectTags()
    {
        // Arrange
        var pages = new Dictionary<int, Page>
    {
        { 0, new Page(new Document(new FileMetadata("1234", "Document A.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Text = "Page 1 Text" } },
        { 1, new Page(new Document(new FileMetadata("5678", "Document B.pdf", 100, StorageProvider.Database, "file2"), MediaType.ApplicationPdf), 3, 3) { Text = "Page 3 Text" } },
        { 2, new Page(new Document(new FileMetadata("9012", "Document C.pdf", 100, StorageProvider.Database, "file3"), MediaType.ApplicationPdf), 2, 2) { Text = "Page 2 Text" } }
    };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Document-A.pdf)>", result);
        Assert.Contains("Page 1 Text", result);
        Assert.Contains("</page1(Document-A.pdf)>", result);

        Assert.Contains("<page2(Document-B.pdf)>", result);
        Assert.Contains("Page 3 Text", result);
        Assert.Contains("</page2(Document-B.pdf)>", result);

        Assert.Contains("<page3(Document-C.pdf)>", result);
        Assert.Contains("Page 2 Text", result);
        Assert.Contains("</page3(Document-C.pdf)>", result);
    }

    [Fact]
    public void GetTextsInputContext_WhenCalledWithEmptyDictionary_ReturnsEmptyString()
    {
        // Arrange
        var pages = new Dictionary<int, Page>();

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetTextsInputContext_WhenCalledWithSinglePage_ReturnsCorrectFormat()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 5, 5) { Text = "Single page content" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("Single page content", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        Assert.Equal(
            """
            <page1(Test-Document.pdf)>
            Single page content
            </page1(Test-Document.pdf)>
            """,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetTextsInputContext_WhenDocumentNameHasSpaces_ReplacesSpacesWithDashes()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "My Test Document With Spaces.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Text = "Content" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(My-Test-Document-With-Spaces.pdf)>", result);
        Assert.Contains("Content", result);
        Assert.Contains("</page1(My-Test-Document-With-Spaces.pdf)>", result);
    }

    [Fact]
    public void GetTextsInputContext_WhenDocumentIsNull_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1)
        {
            Text = "Content without document",
            Document = null
        };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1()>", result);
        Assert.Contains("Content without document", result);
        Assert.Contains("</page1()>", result);
    }

    [Fact]
    public void GetTextsInputContext_WhenPageTextIsNull_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Text = null };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        string expectedResult =
        """
        <page1(Test-Document.pdf)>

        </page1(Test-Document.pdf)>
        """;
        Assert.Equal(
            expectedResult,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetTextsInputContext_WhenPageIsImageOnlyAndUsingReadableText_RendersOverview()
    {
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "Image-derived overview" };
        page.Status = PageStatus.OCRNeedsReview;
        var pages = new Dictionary<int, Page> { { 0, page } };

        string result = PagesHelper.GetTextsInputContext(pages, useReadableText: true);

        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("Image-derived overview", result);
    }

    [Fact]
    public void GetTextsInputContext_WhenPageIsImageOnlyAndDefault_OmitsOverview()
    {
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "Image-derived overview" };
        page.Status = PageStatus.OCRNeedsReview;
        var pages = new Dictionary<int, Page> { { 0, page } };

        string result = PagesHelper.GetTextsInputContext(pages);

        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.DoesNotContain("Image-derived overview", result);
    }

    [Fact]
    public void GetTextsInputContext_WhenPageTextIsEmpty_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Text = "" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        string expectedResult =
        """
        <page1(Test-Document.pdf)>

        </page1(Test-Document.pdf)>
        """;
        Assert.Equal(
            expectedResult,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetTextsInputContext_WhenCalledWithMultiplePages_NumbersSequentially()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "Test Doc.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf);
        var pages = new Dictionary<int, Page>
    {
        { 0, new Page(document, 10, 10) { Text = "Page 10 content" } },
        { 1, new Page(document, 5, 5) { Text = "Page 5 content" } },
        { 2, new Page(document, 15, 15) { Text = "Page 15 content" } }
    };

        // Act
        string result = PagesHelper.GetTextsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Doc.pdf)>", result);
        Assert.Contains("<page2(Test-Doc.pdf)>", result);
        Assert.Contains("<page3(Test-Doc.pdf)>", result);

        Assert.Contains("Page 10 content", result);
        Assert.Contains("Page 5 content", result);
        Assert.Contains("Page 15 content", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithPages_ReturnsOverviewsWithCorrectTags()
    {
        // Arrange
        var pages = new Dictionary<int, Page>
        {
            { 0, new Page(new Document(new FileMetadata("1234", "Document A.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "Page 1 Overview" } },
            { 1, new Page(new Document(new FileMetadata("5678", "Document B.pdf", 100, StorageProvider.Database, "file2"), MediaType.ApplicationPdf), 3, 3) { Overview = "Page 3 Overview" } },
            { 2, new Page(new Document(new FileMetadata("9012", "Document C.pdf", 100, StorageProvider.Database, "file3"), MediaType.ApplicationPdf), 2, 2) { Overview = "Page 2 Overview" } }
        };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Document-A.pdf)>", result);
        Assert.Contains("Page 1 Overview", result);
        Assert.Contains("</page1(Document-A.pdf)>", result);

        Assert.Contains("<page2(Document-B.pdf)>", result);
        Assert.Contains("Page 3 Overview", result);
        Assert.Contains("</page2(Document-B.pdf)>", result);

        Assert.Contains("<page3(Document-C.pdf)>", result);
        Assert.Contains("Page 2 Overview", result);
        Assert.Contains("</page3(Document-C.pdf)>", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithEmptyDictionary_ReturnsEmptyString()
    {
        // Arrange
        var pages = new Dictionary<int, Page>();

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithSinglePage_ReturnsCorrectFormat()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 5, 5) { Overview = "Single page overview" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("Single page overview", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        Assert.Equal(
            """
            <page1(Test-Document.pdf)>
            Single page overview
            </page1(Test-Document.pdf)>
            """,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenDocumentNameHasSpaces_ReplacesSpacesWithDashes()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "My Test Document With Spaces.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "Overview content" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(My-Test-Document-With-Spaces.pdf)>", result);
        Assert.Contains("Overview content", result);
        Assert.Contains("</page1(My-Test-Document-With-Spaces.pdf)>", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenDocumentIsNull_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1)
        {
            Overview = "Overview without document",
            Document = null
        };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1()>", result);
        Assert.Contains("Overview without document", result);
        Assert.Contains("</page1()>", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenPageOverviewIsNull_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = null };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        string expectedResult =
        """
    <page1(Test-Document.pdf)>

    </page1(Test-Document.pdf)>
    """;
        Assert.Equal(
            expectedResult,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenPageOverviewIsEmpty_HandlesGracefully()
    {
        // Arrange
        var page = new Page(new Document(new FileMetadata("1234", "Test Document.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "" };
        var pages = new Dictionary<int, Page> { { 0, page } };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Document.pdf)>", result);
        Assert.Contains("</page1(Test-Document.pdf)>", result);
        string expectedResult =
        """
        <page1(Test-Document.pdf)>

        </page1(Test-Document.pdf)>
        """;
        Assert.Equal(
            expectedResult,
            result,
            ignoreLineEndingDifferences: true,
            ignoreWhiteSpaceDifferences: true,
            ignoreCase: false);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithMultiplePages_NumbersSequentially()
    {
        // Arrange
        var document = new Document(new FileMetadata("1234", "Test Doc.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf);
        var pages = new Dictionary<int, Page>
        {
            { 0, new Page(document, 10, 10) { Overview = "Page 10 overview" } },
            { 1, new Page(document, 5, 5) { Overview = "Page 5 overview" } },
            { 2, new Page(document, 15, 15) { Overview = "Page 15 overview" } }
        };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Test-Doc.pdf)>", result);
        Assert.Contains("<page2(Test-Doc.pdf)>", result);
        Assert.Contains("<page3(Test-Doc.pdf)>", result);

        Assert.Contains("Page 10 overview", result);
        Assert.Contains("Page 5 overview", result);
        Assert.Contains("Page 15 overview", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithMixedOverviews_ReturnsCorrectContent()
    {
        // Arrange
        var pages = new Dictionary<int, Page>
        {
            { 0, new Page(new Document(new FileMetadata("1234", "Doc1.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "First overview" } },
            { 1, new Page(new Document(new FileMetadata("5678", "Doc2.pdf", 100, StorageProvider.Database, "file2"), MediaType.ApplicationPdf), 2, 2) { Overview = null } },
            { 2, new Page(new Document(new FileMetadata("9012", "Doc3.pdf", 100, StorageProvider.Database, "file3"), MediaType.ApplicationPdf), 3, 3) { Overview = "Third overview" } },
            { 3, new Page(new Document(new FileMetadata("1357", "Doc4.pdf", 100, StorageProvider.Database, "file4"), MediaType.ApplicationPdf), 4, 4) { Overview = "" } }
        };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        Assert.Contains("<page1(Doc1.pdf)>", result);
        Assert.Contains("First overview", result);
        Assert.Contains("</page1(Doc1.pdf)>", result);

        Assert.Contains("<page2(Doc2.pdf)>", result);
        Assert.Contains("</page2(Doc2.pdf)>", result);

        Assert.Contains("<page3(Doc3.pdf)>", result);
        Assert.Contains("Third overview", result);
        Assert.Contains("</page3(Doc3.pdf)>", result);

        Assert.Contains("<page4(Doc4.pdf)>", result);
        Assert.Contains("</page4(Doc4.pdf)>", result);

        // Verify that null/empty overviews still create tags but with empty content
        Assert.DoesNotContain("null", result);
    }

    [Fact]
    public void GetOverviewsInputContext_WhenCalledWithUnorderedDictionary_OrdersByKey()
    {
        // Arrange
        var pages = new Dictionary<int, Page>
        {
            { 2, new Page(new Document(new FileMetadata("1234", "Doc.pdf", 100, StorageProvider.Database, "file1"), MediaType.ApplicationPdf), 1, 1) { Overview = "Third in order" } },
            { 0, new Page(new Document(new FileMetadata("5678", "Doc.pdf", 100, StorageProvider.Database, "file2"), MediaType.ApplicationPdf), 2, 2) { Overview = "First in order" } },
            { 1, new Page(new Document(new FileMetadata("9012", "Doc.pdf", 100, StorageProvider.Database, "file3"), MediaType.ApplicationPdf), 3, 3) { Overview = "Second in order" } }
        };

        // Act
        string result = PagesHelper.GetOverviewsInputContext(pages);

        // Assert
        // Check that the pages appear in the correct order based on dictionary key
        int firstPageIndex = result.IndexOf("<page1(", StringComparison.Ordinal);
        int secondPageIndex = result.IndexOf("<page2(", StringComparison.Ordinal);
        int thirdPageIndex = result.IndexOf("<page3(", StringComparison.Ordinal);

        Assert.True(firstPageIndex < secondPageIndex);
        Assert.True(secondPageIndex < thirdPageIndex);

        Assert.Contains("First in order", result);
        Assert.Contains("Second in order", result);
        Assert.Contains("Third in order", result);
    }
}
