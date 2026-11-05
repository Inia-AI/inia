using ErrorOr;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.Queries.Documents.ListDocuments;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Enums;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.WebApi.UnitTests.ODataLists;

public class DocumentListsTests : ODataListTests
{
    private const string ORGANIZATION_ID = "org-documents";
    private const string USER_ID = "user-documents";
    private const string DOCUMENT_ID = "document-pages";

    private static readonly DateTime _created = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheDocumentList_ReturnsTheSameJson()
    {
        await SeedDocumentsAsync();

        AssertMatchesFixture(await ListDocumentsAsync("?$orderby=name"), "Documents");
    }

    [Fact]
    public async Task TheDocumentList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedDocumentsAsync();

        ODataPaginatedResponse<DocumentSummaryDto> page = await ListDocumentsAsync(
            "?$filter=sizeBytes gt 0 and processingError eq null&$orderby=createdAt desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["processed.pdf"], page.Value.Select(document => document.Name));
        Assert.Equal(2, page.Count);
        Assert.Equal(1, page.Skip);
        Assert.Equal(1, page.Top);
    }

    [Fact]
    public async Task ThePageList_ReturnsTheSameJson()
    {
        await SeedPagesAsync();

        AssertMatchesFixture(await ListPagesAsync("?$orderby=pageNumber"), "DocumentPages");
    }

    [Fact]
    public async Task ThePageList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedPagesAsync();

        ODataPaginatedResponse<PageSummaryDto> page = await ListPagesAsync(
            "?$filter=hasEmbedding or ocrNeedsReview&$orderby=pageNumber desc&$top=1&$skip=1&$count=true");

        Assert.Equal([1], page.Value.Select(item => item.PageNumber));
        Assert.Equal(2, page.Count);
    }

    private async Task<ODataPaginatedResponse<DocumentSummaryDto>> ListDocumentsAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await Handler<PaginatedResponseDocumentsQueryHandler>().Handle(
            new PaginatedResponseDocumentsQuery(ORGANIZATION_ID, USER_ID, Options<DocumentSummaryDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<PageSummaryDto>> ListPagesAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await Handler<PaginatedResponseDocumentPagesQueryHandler>().Handle(
            new PaginatedResponseDocumentPagesQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID, Options<PageSummaryDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task SeedDocumentsAsync()
    {
        Document processed = BuildDocument("document-processed", "processed.pdf", 2048, 0);
        processed.RequestProcessing(_created.AddMinutes(1));
        processed.RecordProcessed(_created.AddMinutes(2));

        Document queued = BuildDocument("document-queued", "queued.pdf", 4096, 1);
        queued.RequestProcessing(_created.AddMinutes(3));

        Document failed = BuildDocument("document-failed", "failed.pdf", 512, 2);
        failed.RequestProcessing(_created.AddMinutes(4));
        failed.RecordProcessingFailure("The scan is unreadable.");

        Document unprocessed = BuildDocument("document-unprocessed", "unprocessed.pdf", 0, 3);

        Document otherOrganization = BuildDocument("document-other", "other.pdf", 100, 4);
        otherOrganization.OrganizationId = "org-other";

        UserData.Documents.AddRange(processed, queued, failed, unprocessed, otherOrganization);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedPagesAsync()
    {
        Document document = BuildDocument(DOCUMENT_ID, "pages.pdf", 1024, 0);
        var embedding = new EmbeddingVector("cohere-embed-v4", [0.1, 0.2]);
        UserData.Entry(embedding).Property(vector => vector.Id).CurrentValue = "embedding-1";

        Page embedded = BuildPage(document, "page-1", 1, PageStatus.ProcessedSuccessfully);
        embedded.Text = new string('a', 250);
        embedded.Overview = "A balance sheet.";
        embedded.EmbeddingVector = embedding;
        embedded.EmbeddingVectorId = embedding.Id;

        Page needsReview = BuildPage(document, "page-2", 2, PageStatus.OCRNeedsReview);
        needsReview.RawText = "Short raw text.";
        needsReview.Location = PageLocation.LeftSide;
        needsReview.SourceUrl = "https://example.com/source/2";

        Page empty = BuildPage(document, "page-3", 3, PageStatus.NotProcessed);

        UserData.Documents.Add(document);
        UserData.Pages.AddRange(embedded, needsReview, empty);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Page BuildPage(Document document, string id, int pageNumber, PageStatus status)
    {
        return WithId(new Page(document, pageNumber, pageNumber + 10) { Status = status }, id);
    }

    private static Document BuildDocument(string id, string name, long sizeBytes, int order)
    {
        var fileMetadata = new FileMetadata(ORGANIZATION_ID, name, sizeBytes, StorageProvider.Database)
        {
            Id = $"file-{id}",
            CreatedAt = _created,
            UpdatedAt = _created,
        };

        return new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            Id = id,
            OrganizationId = ORGANIZATION_ID,
            Name = name,
            UserId = USER_ID,
            WorkflowTypeId = "Corp",
            DocumentCategoryId = "category-financials",
            Class = "Annual Report",
            WasPasswordProtected = order % 2 == 0,
            CreatedAt = _created.AddHours(order),
            UpdatedAt = _created.AddHours(order).AddMinutes(30),
        };
    }
}
