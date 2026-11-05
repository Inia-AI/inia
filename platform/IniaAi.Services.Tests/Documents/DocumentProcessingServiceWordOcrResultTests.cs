using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Services.Documents;
using static IniaAi.Core.Services.OcrTranscriptionService;

namespace IniaAi.Services.Tests.Documents;

public class DocumentProcessingServiceWordOcrResultTests
{
    private const string NATIVE_TEXT = "Native narrative extracted from the Word page.";

    [Fact]
    public void ApplyWordOcrResultsToPage_NoRegions_KeepsNativeTextAsProcessed()
    {
        Page page = CreatePage();

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, []);

        Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status);
        Assert.Equal(NATIVE_TEXT, page.RawText);
        Assert.Equal(NATIVE_TEXT, page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_AllRegionsSucceed_AppendsTranscriptionsInOrder()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = true, MarkdownResults = "## Pasted table A" },
            new(1) { Succeeded = true, MarkdownResults = "## Pasted chart B" },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Equal(NATIVE_TEXT, page.RawText);
        Assert.Equal($"{NATIVE_TEXT}\n\n## Pasted table A\n\n## Pasted chart B", page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_ImageOnlyPage_TextIsTheTranscription()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = true, MarkdownResults = "## The whole page is one pasted table" },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, string.Empty, results);

        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Equal("## The whole page is one pasted table", page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_AnyRegionFails_MarksPageFailedAndDropsText()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = true, MarkdownResults = "## Transcribed fine" },
            new(1) { Succeeded = false, NeedsReview = false },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.OCRFailed, page.Status);
        Assert.Null(page.Text);
        Assert.Null(page.RawText);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_ContentFilteredRegion_IsSkippedAndNativeTextKept()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = false, NeedsReview = true },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status);
        Assert.Equal(NATIVE_TEXT, page.RawText);
        Assert.Equal(NATIVE_TEXT, page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_ContentFilteredPlusSucceeded_MergesOnlySucceeded()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = false, NeedsReview = true },
            new(1) { Succeeded = true, MarkdownResults = "## Clean pasted table" },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Equal($"{NATIVE_TEXT}\n\n## Clean pasted table", page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_EmptyTranscriptionRegion_KeepsNativeTextAsProcessed()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = false, EmptyTranscription = true },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status);
        Assert.Equal(NATIVE_TEXT, page.RawText);
        Assert.Equal(NATIVE_TEXT, page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_EmptyTranscriptionPlusSucceeded_MergesOnlySucceeded()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = false, EmptyTranscription = true },
            new(1) { Succeeded = true, MarkdownResults = "## Table next to a signature scan" },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Equal($"{NATIVE_TEXT}\n\n## Table next to a signature scan", page.Text);
    }

    [Fact]
    public void ApplyWordOcrResultsToPage_SucceededWithBlankMarkdown_DoesNotEmitBlankBlock()
    {
        Page page = CreatePage();
        List<OcrProcessingResult> results =
        [
            new(1) { Succeeded = true, MarkdownResults = "   " },
        ];

        DocumentProcessingService.ApplyWordOcrResultsToPage(page, NATIVE_TEXT, results);

        Assert.Equal(PageStatus.ProcessedSuccessfully, page.Status);
        Assert.Equal(NATIVE_TEXT, page.Text);
    }

    private static Page CreatePage()
    {
        var document = new Document(new FileMetadata(
            "IniaAi",
            "Doc.docx",
            123,
            StorageProvider.Database,
            Guid.NewGuid().ToString()), MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument);

        return new Page(document, 1, 1);
    }
}
