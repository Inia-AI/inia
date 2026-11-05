using ErrorOr;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.Queries.Documents.ListDocuments;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.Documents;

/// <summary>
/// End-to-end integration tests for the Documents API. Exercises the full dispatch
/// pipeline (validator + handler + EF + real Postgres) for each endpoint,
/// covering happy paths, NotFound error shapes (another organization's document answers as an unknown one), bulk semantics, and
/// OData-driven pagination/filtering.
/// </summary>
public class DocumentsTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string ORG_ID = "IniaAI";
    private const string OTHER_ORG_ID = "OrgB-Documents-Integration";
    private const string USER_ID = "test-documents-user";

    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededDocumentIds = [];
    private readonly List<string> _seededFileMetadataIds = [];
    private readonly List<string> _seededDocumentInfoIds = [];
    private readonly List<string> _seededOrganizationIds = [];

    public DocumentsTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_seededDocumentIds.Count == 0
            && _seededFileMetadataIds.Count == 0
            && _seededDocumentInfoIds.Count == 0
            && _seededOrganizationIds.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        if (_seededDocumentInfoIds.Count > 0)
        {
            _ = await db.Set<DocumentInfo>()
                .IgnoreQueryFilters()
                .Where(di => _seededDocumentInfoIds.Contains(di.Id))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        if (_seededDocumentIds.Count > 0)
        {
            _ = await db.Pages
                .IgnoreQueryFilters()
                .Where(p => _seededDocumentIds.Contains(p.DocumentId))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

            _ = await db.Documents
                .IgnoreQueryFilters()
                .Where(d => _seededDocumentIds.Contains(d.Id))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        if (_seededFileMetadataIds.Count > 0)
        {
            _ = await db.FileMetadata
                .IgnoreQueryFilters()
                .Where(f => _seededFileMetadataIds.Contains(f.Id))
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await _apiBuilder.CleanOrganizationsByIdsAsync(_seededOrganizationIds);
    }

    [Fact]
    public async Task GetDocument_WithOwnedId_ReturnsDetail()
    {
        Document document = await SeedDocumentAsync($"DOC-Get-{Guid.NewGuid():N}");

        var query = new GetDocumentQuery(ORG_ID, USER_ID, document.Id);
        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Equal(document.Id, result.Value.Id);
        Assert.Equal(document.Name, result.Value.Name);
    }

    [Fact]
    public async Task GetDocument_WithUnknownId_ReturnsNotFound()
    {
        var query = new GetDocumentQuery(ORG_ID, USER_ID, $"missing-doc-{Guid.NewGuid():N}");
        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task GetDocument_CrossOrg_ReturnsNotFound()
    {
        Document document = await SeedDocumentAsync(
            $"DOC-Get-CrossOrg-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        var query = new GetDocumentQuery(ORG_ID, USER_ID, document.Id);
        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ListDocuments_ReturnsOnlyCallerOrg()
    {
        string ownedMarker = $"DOC-List-Owned-{Guid.NewGuid():N}";
        string otherMarker = $"DOC-List-Other-{Guid.NewGuid():N}";
        _ = await SeedDocumentAsync(ownedMarker);
        _ = await SeedDocumentAsync(otherMarker, organizationId: OTHER_ORG_ID);

        ODataQueryOptions<DocumentSummaryDto> options = BuildDocumentOptions(
            $"?$top=100&$filter=name eq '{ownedMarker}' or name eq '{otherMarker}'");

        var query = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, options);
        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Value, d => d.Name == ownedMarker);
        Assert.DoesNotContain(result.Value.Value, d => d.Name == otherMarker);
    }

    [Fact]
    public async Task ListDocuments_PaginatesViaTopAndSkip()
    {
        string uniqueClass = $"class-{Guid.NewGuid():N}";
        _ = await SeedDocumentAsync($"DOC-Page-1-{Guid.NewGuid():N}", cls: uniqueClass);
        _ = await SeedDocumentAsync($"DOC-Page-2-{Guid.NewGuid():N}", cls: uniqueClass);
        _ = await SeedDocumentAsync($"DOC-Page-3-{Guid.NewGuid():N}", cls: uniqueClass);

        ODataQueryOptions<DocumentSummaryDto> first = BuildDocumentOptions(
            $"?$top=2&$skip=0&$orderby=name asc&$filter=class eq '{uniqueClass}'&$count=true");
        ODataQueryOptions<DocumentSummaryDto> second = BuildDocumentOptions(
            $"?$top=2&$skip=2&$orderby=name asc&$filter=class eq '{uniqueClass}'&$count=true");

        var firstQuery = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, first);
        var secondQuery = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, second);

        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> page1 = await _apiBuilder.SendAsync(firstQuery);
        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> page2 = await _apiBuilder.SendAsync(secondQuery);

        Assert.False(page1.IsError);
        Assert.False(page2.IsError);
        Assert.Equal(2, page1.Value.Value.Count);
        Assert.Single(page2.Value.Value);
    }

    [Fact]
    public async Task ListDocuments_FilteredByJobId_ReturnsOnlyMatchingDocuments()
    {
        string jobId = $"job-{Guid.NewGuid():N}";
        string otherJobId = $"job-{Guid.NewGuid():N}";

        Document linked = await SeedDocumentAsync($"DOC-Job-Linked-{Guid.NewGuid():N}");
        Document otherJobDoc = await SeedDocumentAsync($"DOC-Job-Other-{Guid.NewGuid():N}");
        Document unlinked = await SeedDocumentAsync($"DOC-Job-Unlinked-{Guid.NewGuid():N}");

        await SeedDocumentInfoAsync(linked.Id, jobId);
        await SeedDocumentInfoAsync(otherJobDoc.Id, otherJobId);

        ODataQueryOptions<DocumentSummaryDto> options = BuildDocumentOptions("?$top=100");
        var query = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, options, jobId);

        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Value, d => d.Id == linked.Id);
        Assert.DoesNotContain(result.Value.Value, d => d.Id == otherJobDoc.Id);
        Assert.DoesNotContain(result.Value.Value, d => d.Id == unlinked.Id);
    }

    [Fact]
    public async Task ListDocuments_FilteredByJobId_LeavesOutADocumentTheRunDropped()
    {
        string jobId = $"job-{Guid.NewGuid():N}";
        Document kept = await SeedDocumentAsync($"DOC-Job-Kept-{Guid.NewGuid():N}");
        Document dropped = await SeedDocumentAsync($"DOC-Job-Dropped-{Guid.NewGuid():N}");
        await SeedDocumentInfoAsync(kept.Id, jobId);
        await SeedDocumentInfoAsync(dropped.Id, jobId, exclusionReason: "NotExpectedDocument");

        ODataQueryOptions<DocumentSummaryDto> options = BuildDocumentOptions("?$top=100");
        var query = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, options, jobId);

        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Value, d => d.Id == kept.Id);
        Assert.DoesNotContain(result.Value.Value, d => d.Id == dropped.Id);
    }

    [Fact]
    public async Task ListDocuments_FilteredByUnknownJobId_ReturnsEmpty()
    {
        _ = await SeedDocumentAsync($"DOC-Job-Noise-{Guid.NewGuid():N}");

        string unknownJobId = $"job-unknown-{Guid.NewGuid():N}";
        ODataQueryOptions<DocumentSummaryDto> options = BuildDocumentOptions("?$top=100");
        var query = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, options, unknownJobId);

        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Value);
    }

    [Fact]
    public async Task ListDocuments_FilteredByJobId_DoesNotLeakCrossOrgDocuments()
    {
        string jobId = $"job-{Guid.NewGuid():N}";
        Document otherOrgDoc = await SeedDocumentAsync(
            $"DOC-Job-CrossOrg-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);
        await SeedDocumentInfoAsync(otherOrgDoc.Id, jobId);

        ODataQueryOptions<DocumentSummaryDto> options = BuildDocumentOptions("?$top=100");
        var query = new PaginatedResponseDocumentsQuery(ORG_ID, USER_ID, options, jobId);

        ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        Assert.DoesNotContain(result.Value.Value, d => d.Id == otherOrgDoc.Id);
    }

    [Fact]
    public async Task UpdateDocument_PatchesProvidedFields()
    {
        Document document = await SeedDocumentAsync($"DOC-Update-{Guid.NewGuid():N}");

        var command = new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: document.Id,
            Name: null,
            Description: "Updated description",
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: "Contract",
            Topics: null,
            TableOfContents: null);

        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal("Contract", result.Value.Class);
        Assert.Equal(document.Name, result.Value.Name);
    }

    [Fact]
    public async Task UpdateDocument_UnknownId_ReturnsNotFound()
    {
        var command = new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: $"missing-doc-{Guid.NewGuid():N}",
            Name: "Doesn't matter",
            Description: null,
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: null,
            Topics: null,
            TableOfContents: null);

        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task UpdateDocument_CrossOrg_ReturnsNotFound()
    {
        Document document = await SeedDocumentAsync(
            $"DOC-Update-CrossOrg-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        var command = new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: document.Id,
            Name: "Takeover attempt",
            Description: null,
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: null,
            Topics: null,
            TableOfContents: null);

        ErrorOr<DocumentDetailDto> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DeleteDocument_OwnedId_SoftDeletesRowAndHidesFromGet()
    {
        Document document = await SeedDocumentAsync($"DOC-Delete-{Guid.NewGuid():N}");

        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id);
        ErrorOr<Deleted> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError);

        // Soft-delete is applied at the repository layer (Entity<T>.IsDeleted), so we
        // verify both: the raw row is marked deleted, and a subsequent GET is filtered
        // out by the same app-layer predicate the real callers use.
        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Document? raw = await db.Documents
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(raw);
        Assert.True(raw!.IsDeleted);

        ErrorOr<DocumentDetailDto> afterDelete = await _apiBuilder.SendAsync(
            new GetDocumentQuery(ORG_ID, USER_ID, document.Id));
        Assert.True(afterDelete.IsError);
        Assert.Equal(ErrorType.NotFound, afterDelete.FirstError.Type);
    }

    [Fact]
    public async Task DeleteDocument_UnknownId_ReturnsNotFound()
    {
        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, $"missing-doc-{Guid.NewGuid():N}");
        ErrorOr<Deleted> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DeleteDocument_CrossOrg_ReturnsNotFoundAndKeepsRow()
    {
        Document document = await SeedDocumentAsync(
            $"DOC-Delete-CrossOrg-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id);
        ErrorOr<Deleted> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Document? reloaded = await db.Documents
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsDeleted);
    }

    [Fact]
    public async Task BulkDeleteDocuments_MixedResults_ReturnsPerIdOutcomes()
    {
        Document owned = await SeedDocumentAsync($"DOC-Bulk-Owned-{Guid.NewGuid():N}");
        Document crossOrg = await SeedDocumentAsync(
            $"DOC-Bulk-Cross-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);
        string missingId = $"missing-{Guid.NewGuid():N}";

        var command = new BulkDeleteDocumentsCommand(
            ORG_ID,
            USER_ID,
            [owned.Id, crossOrg.Id, missingId]);

        ErrorOr<BulkDeleteDocumentsResultDto> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError);
        Assert.Contains(owned.Id, result.Value.Deleted);
        Assert.True(result.Value.Failed.ContainsKey(crossOrg.Id));
        Assert.True(result.Value.Failed.ContainsKey(missingId));
    }

    [Fact]
    public async Task ListDocumentPages_ReturnsSeededPages()
    {
        Document document = await SeedDocumentAsync($"DOC-Pages-{Guid.NewGuid():N}");
        await SeedPageAsync(document.Id, pageNumber: 1, text: "Alpha page");
        await SeedPageAsync(document.Id, pageNumber: 2, text: "Beta page");

        ODataQueryOptions<PageSummaryDto> options = BuildPageOptions("?$top=10&$orderby=pageNumber asc");
        var query = new PaginatedResponseDocumentPagesQuery(ORG_ID, USER_ID, document.Id, options);

        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError);
        List<PageSummaryDto> items = result.Value.Value;
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].PageNumber);
        Assert.Equal(2, items[1].PageNumber);
    }

    [Fact]
    public async Task ListDocumentPages_UnknownDocument_ReturnsNotFound()
    {
        ODataQueryOptions<PageSummaryDto> options = BuildPageOptions("?$top=10");
        var query = new PaginatedResponseDocumentPagesQuery(
            ORG_ID,
            USER_ID,
            $"missing-doc-{Guid.NewGuid():N}",
            options);

        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ListDocumentPages_CrossOrg_ReturnsNotFound()
    {
        Document document = await SeedDocumentAsync(
            $"DOC-Pages-Cross-{Guid.NewGuid():N}",
            organizationId: OTHER_ORG_ID);

        ODataQueryOptions<PageSummaryDto> options = BuildPageOptions("?$top=10");
        var query = new PaginatedResponseDocumentPagesQuery(ORG_ID, USER_ID, document.Id, options);

        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await _apiBuilder.SendAsync(query);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task<Document> SeedDocumentAsync(
        string name,
        string organizationId = ORG_ID,
        string? cls = null)
    {
        await EnsureOrganizationExistsAsync(organizationId);

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        // SizeBytes is intentionally small — the blob is never actually stored since
        // delete tests verify only the DB side (external storage is best-effort and
        // there is no blob to remove for in-test seeded rows).
        var fileMetadata = new FileMetadata(
            organizationId,
            $"{name}.pdf",
            sizeBytes: 1024,
            storageProvider: StorageProvider.Database);

        var document = new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            OrganizationId = organizationId,
            Name = name,
            Class = cls,
        };

        _ = await db.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);
        _ = await db.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededDocumentIds.Add(document.Id);
        _seededFileMetadataIds.Add(fileMetadata.Id);
        return document;
    }

    private async Task SeedDocumentInfoAsync(string documentId, string jobId, string? exclusionReason = null)
    {
        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        var info = new DocumentInfo(documentId)
        {
            JobId = jobId,
        };

        if (exclusionReason is not null)
        {
            info.ExcludeFromRun(exclusionReason, DateTime.UtcNow);
        }

        _ = await db.Set<DocumentInfo>().AddAsync(info, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededDocumentInfoIds.Add(info.Id);
    }

    private async Task SeedPageAsync(string documentId, int pageNumber, string text)
    {
        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        Document document = await db.Documents
            .IgnoreQueryFilters()
            .SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);

        var page = new Page(document, pageNumber, filePageNumber: pageNumber)
        {
            Text = text,
            Status = PageStatus.ProcessedSuccessfully,
        };

        _ = await db.Pages.AddAsync(page, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task EnsureOrganizationExistsAsync(string organizationId)
    {
        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        bool exists = await db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

        if (exists)
        {
            return;
        }

        var organization = new Organization($"Test Org {organizationId}", customId: organizationId);
        _ = await db.Organizations.AddAsync(organization, TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _seededOrganizationIds.Add(organization.Id);
    }

    private static ODataQueryOptions<DocumentSummaryDto> BuildDocumentOptions(string queryString)
    {
        IEdmModel model = BuildDocumentEdmModel();
        return ODataTestHelper.BuildFakeODataOptions<DocumentSummaryDto>(model, queryString);
    }

    private static ODataQueryOptions<PageSummaryDto> BuildPageOptions(string queryString)
    {
        IEdmModel model = BuildPageEdmModel();
        return ODataTestHelper.BuildFakeODataOptions<PageSummaryDto>(model, queryString);
    }

    private static IEdmModel BuildDocumentEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        _ = builder.EntitySet<DocumentSummaryDto>("Documents");
        return builder.GetEdmModel();
    }

    private static IEdmModel BuildPageEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        _ = builder.EntitySet<PageSummaryDto>("Pages");
        return builder.GetEdmModel();
    }
}
