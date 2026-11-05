using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Services.Documents;
using static IniaAi.Core.Services.OcrTranscriptionService;

namespace IniaAi.Services.Tests.Documents;

public class DocumentProcessingServiceOcrResultTests
{
    [Fact]
    public void ApplyOcrResultToPage_NeedsReview_FlagsPageAndDropsAllText()
    {
        Page page = CreatePage(PageStatus.OCRFallback, rawText: "bad native text", text: "bad native text");
        OcrProcessingResult result = new(1)
        {
            NeedsReview = true,
            Succeeded = true,
            MarkdownResults = "should never be stored",
        };

        DocumentProcessingService.ApplyOcrResultToPage(page, result);

        Assert.Equal(PageStatus.OCRNeedsReview, page.Status);
        Assert.Null(page.Text);
        Assert.Null(page.RawText);
    }

    [Fact]
    public void ApplyOcrResultToPage_Refused_FlagsThePageWithItsCategoryAndNoText()
    {
        Page page = CreatePage(PageStatus.OCRFallback, rawText: "native text", text: "native text");
        OcrProcessingResult result = new(1)
        {
            NeedsReview = true,
            Refusal = new ModelRefusal(ModelRefusal.WRITTEN_CATEGORY, "I can't transcribe this identity card.", null, null),
        };

        DocumentProcessingService.ApplyOcrResultToPage(page, result);

        Assert.Equal(PageStatus.OCRNeedsReview, page.Status);
        Assert.Equal(ModelRefusal.WRITTEN_CATEGORY, page.RefusalCategory);
        Assert.Null(page.Text);
        Assert.Null(page.ReadableText);
    }

    [Fact]
    public void ApplyOcrResultToPage_ASuccessAfterARefusal_ClearsTheFlag()
    {
        Page page = CreatePage(PageStatus.OCRNeedsReview, rawText: null, text: null);
        page.RefusalCategory = "general_harms";

        DocumentProcessingService.ApplyOcrResultToPage(page, new OcrProcessingResult(1) { Succeeded = true, MarkdownResults = "## Page" });

        Assert.Null(page.RefusalCategory);
        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
    }

    [Fact]
    public void ApplyOcrResultToPage_NeedsReview_ClearsStaleOverviewAndEmbedding()
    {
        Page page = CreatePage(PageStatus.ExtractedWithErrors, rawText: "garbled", text: "garbled");
        page.Overview = "overview derived from garbled text";
        page.EmbeddingVector = new EmbeddingVector("test-model", [0.1, 0.2]);
        page.EmbeddingVectorId = "stale-vector-id";
        OcrProcessingResult result = new(1)
        {
            NeedsReview = true,
        };

        DocumentProcessingService.ApplyOcrResultToPage(page, result);

        Assert.Null(page.Overview);
        Assert.Null(page.EmbeddingVector);
        Assert.Null(page.EmbeddingVectorId);
    }

    [Fact]
    public void ApplyOcrResultToPage_Succeeded_StoresTranscriptionAndDropsStaleRawText()
    {
        Page page = CreatePage(PageStatus.OCRFallback, rawText: "bad native extraction", text: "bad native extraction");
        OcrProcessingResult result = new(1)
        {
            Succeeded = true,
            MarkdownResults = "## Transcribed markdown",
        };

        DocumentProcessingService.ApplyOcrResultToPage(page, result);

        Assert.Equal(PageStatus.OCRSuccessful, page.Status);
        Assert.Equal("## Transcribed markdown", page.Text);
        Assert.Equal("## Transcribed markdown", page.RawText);
    }

    [Fact]
    public void ApplyOcrResultToPage_Failed_MarksFailedAndDropsAllText()
    {
        Page page = CreatePage(PageStatus.OCRFallback, rawText: "partial", text: "partial");
        OcrProcessingResult result = new(1)
        {
            Succeeded = false,
            NeedsReview = false,
        };

        DocumentProcessingService.ApplyOcrResultToPage(page, result);

        Assert.Equal(PageStatus.OCRFailed, page.Status);
        Assert.Null(page.Text);
        Assert.Null(page.RawText);
    }

    [Fact]
    public void IsPageProcessed_OcrNeedsReviewWithoutText_TreatedAsProcessed()
    {
        Page page = CreatePage(PageStatus.OCRNeedsReview, rawText: null, text: null);

        Assert.True(DocumentProcessingService.IsPageProcessed(page));
    }

    [Fact]
    public void IsPageProcessed_OcrFailedWithoutText_NotProcessed()
    {
        Page page = CreatePage(PageStatus.OCRFailed, rawText: null, text: null);

        Assert.False(DocumentProcessingService.IsPageProcessed(page));
    }

    private static Page CreatePage(PageStatus status, string? rawText, string? text)
    {
        var document = new Document(new FileMetadata(
            "IniaAi",
            "Doc.pdf",
            123,
            StorageProvider.Database,
            Guid.NewGuid().ToString()), MediaType.ApplicationPdf);

        return new Page(document, 1, 1)
        {
            RawText = rawText,
            Text = text,
            Status = status,
        };
    }
}
