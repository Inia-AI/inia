using ErrorOr;
using IniaAi.Application.Queries.Documents.GetDocumentPage;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.Documents.GetDocumentPage;

public class GetDocumentPageQueryHandlerTests : DocumentTestHost
{
    private GetDocumentPageQueryHandler Handler()
    {
        return new GetDocumentPageQueryHandler(_userData, NullLogger<GetDocumentPageQueryHandler>.Instance);
    }

    private Task<Document> SeedWithPagesAsync(string organizationId)
    {
        return SeedDocumentWithPagesAsync(
            organizationId,
            "report.pdf",
            first =>
            {
                first.Text = new string('x', 5000);
                first.SectionTitles = "Balance sheet, Notes";
            },
            second =>
            {
                second.Text = "second";
                second.SectionTitles = " ";
                second.Overview = "an overview of a scanned page";
                second.Status = PageStatus.OCRNeedsReview;
            });
    }

    [Fact]
    public async Task Handle_ReturnsTheFullText_NotAPreview()
    {
        Document document = await SeedWithPagesAsync(ORG_ID);

        ErrorOr<PageDetailDto> result = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 1), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(5000, result.Value.Text!.Length);
        Assert.Equal(200, result.Value.TextPreview!.Length);
        Assert.Equal(result.Value.Text, result.Value.ReadableText);
    }

    [Fact]
    public async Task Handle_APageStoredWithOnlyItsRawText_ReadsAsThatText()
    {
        Document document = await SeedDocumentWithPagesAsync(ORG_ID, "report.pdf", page => page.RawText = "raw text only");
        Page stored = _context.Pages.Single(page => page.DocumentId == document.Id);
        _context.Entry(stored).Property(page => page.Text).CurrentValue = null;
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<PageDetailDto> result = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 1), TestContext.Current.CancellationToken);

        Assert.Equal("raw text only", result.Value.Text);
        Assert.Equal("raw text only", result.Value.ReadableText);
    }

    [Fact]
    public async Task Handle_ReturnsTheSectionTitlesTheTableOfContentsGaveThePage_AndNoneForABlankValue()
    {
        Document document = await SeedWithPagesAsync(ORG_ID);

        ErrorOr<PageDetailDto> titled = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 1), TestContext.Current.CancellationToken);
        ErrorOr<PageDetailDto> untitled = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 2), TestContext.Current.CancellationToken);

        Assert.Equal("Balance sheet, Notes", titled.Value.SectionTitles);
        Assert.Null(untitled.Value.SectionTitles);
    }

    [Fact]
    public async Task Handle_AnImageOnlyPageReadsThroughItsOverview()
    {
        Document document = await SeedWithPagesAsync(ORG_ID);

        ErrorOr<PageDetailDto> result = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 2), TestContext.Current.CancellationToken);

        Assert.True(result.Value.OcrNeedsReview);
        Assert.Equal("second", result.Value.Text);
        Assert.Equal("an overview of a scanned page", result.Value.ReadableText);
    }

    [Fact]
    public async Task Handle_ReportsTheEmbeddingModelAndItsDimensions()
    {
        Document document = await SeedDocumentWithPagesAsync(
            ORG_ID,
            "embedded.pdf",
            first => first.EmbeddingVector = new EmbeddingVector("text-embedding-test", [0.1, 0.2, 0.3]),
            second => second.Text = "no vector yet");

        ErrorOr<PageDetailDto> embedded = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 1), TestContext.Current.CancellationToken);
        ErrorOr<PageDetailDto> plain = await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, document.Id, 2), TestContext.Current.CancellationToken);

        Assert.Equal("text-embedding-test", embedded.Value.EmbeddingModel);
        Assert.Equal(3, embedded.Value.EmbeddingDimensions);
        Assert.Null(plain.Value.EmbeddingModel);
        Assert.Null(plain.Value.EmbeddingDimensions);
    }

    [Fact]
    public async Task Handle_MissingPage_UnknownDocument_AndForeignDocument_AreAllNotFound()
    {
        Document own = await SeedWithPagesAsync(ORG_ID);
        Document foreign = await SeedWithPagesAsync(OTHER_ORG_ID);

        Assert.Equal("Document.PageNotFound", (await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, own.Id, 3), TestContext.Current.CancellationToken)).FirstError.Code);
        Assert.Equal(ErrorType.NotFound, (await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, "missing", 1), TestContext.Current.CancellationToken)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await Handler().Handle(new GetDocumentPageQuery(ORG_ID, USER_ID, foreign.Id, 1), TestContext.Current.CancellationToken)).FirstError.Type);
    }
}
