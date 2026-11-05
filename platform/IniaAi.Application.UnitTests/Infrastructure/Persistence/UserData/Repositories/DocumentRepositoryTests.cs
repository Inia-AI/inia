using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Repositories;
using IniaAi.Testing.DependencyInjection;
using System.Text;

namespace IniaAi.Application.UnitTests.Infrastructure.Persistence.UserData.Repositories;

public class DocumentRepositoryTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DocumentRepository _repository;
    private readonly UserDataDbContext _context;

    public DocumentRepositoryTests()
    {
        _serviceProvider = new ServiceProvider();
        _context = (UserDataDbContext)_serviceProvider.GetService(typeof(UserDataDbContext))!;
        _repository = new DocumentRepository(_context);
    }

    [Fact]
    public async Task GetOrCreateDocumentAsync_WhenFileDoesNotExist_ShouldCreateNewDocumentAsync()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("test content");
        string organizationId = "org1";
        string userId = "user1";
        string fileName = "GetOrCreateDocumentAsync_WhenFileDoesNotExist.txt";
        int fileSize = content.Length;

        // Act
        (Document result, bool created) = await _repository.GetOrCreateDocumentAsync(content, organizationId, userId, fileName, fileSize, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(created);
        Assert.NotNull(result);
        Assert.NotNull(result.FileMetadata);
        Assert.StartsWith(result.FileMetadata.Name, fileName);
        Assert.EndsWith(result.FileMetadata.Extension, fileName);
        Assert.Equal(fileSize, result.FileMetadata.SizeBytes);
        Assert.Equal(organizationId, result.FileMetadata.OrganizationId);
    }

    [Fact]
    public async Task GetOrCreateDocumentAsync_WhenFileExists_ShouldReturnExistingDocumentAsync()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("test content");
        string organizationId = "org1";
        string userId = "user1";
        string fileName = "GetOrCreateDocumentAsync_WhenFileExists.txt";
        int fileSize = content.Length;

        // Create initial document
        (Document initialDoc, _) = await _repository.GetOrCreateDocumentAsync(content, organizationId, userId, fileName, fileSize, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        (Document result, bool created) = await _repository.GetOrCreateDocumentAsync(content, organizationId, userId, fileName, fileSize, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);

        // Assert
        Assert.False(created);
        Assert.NotNull(result);
        Assert.Equal(initialDoc.Id, result.Id);
        Assert.Equal(initialDoc.FileMetadata?.Id, result.FileMetadata?.Id);
    }

    [Fact]
    public async Task GetOrCreateDocumentAsync_ANameWithALineBreak_FindsTheDocumentStoredUnderItsOneLineName()
    {
        byte[] content = Encoding.UTF8.GetBytes("test content");
        string fileName = "uploaded\r\ntwice.txt";
        (Document first, _) = await _repository.GetOrCreateDocumentAsync(content, "org1", "user1", fileName, content.Length, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Document second, bool created) = await _repository.GetOrCreateDocumentAsync(content, "org1", "user1", fileName, content.Length, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);

        Assert.False(created);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("uploaded  twice", second.Name);
    }

    [Fact]
    public async Task FindByCounterpartyIdAsync_ShouldReturnDocumentsForCounterpartyAsync()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("test content");
        string organizationId = "org1";
        string userId = "user1";
        string fileName = "FindByCounterpartyIdAsync.txt";
        int fileSize = content.Length;
        var counterparty = new Counterparty(organizationId, "Count1");

        // Create document and add counterparty
        (Document doc, _) = await _repository.GetOrCreateDocumentAsync(content, organizationId, userId, fileName, fileSize, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        doc.Counterparties.Add(counterparty);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IEnumerable<Document> result = await _repository.FindByCounterpartyIdAsync(counterparty.Id);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);
        Assert.Contains(result, d => d.Id == doc.Id);
        Assert.Contains(doc.Counterparties, c => c.Id == counterparty.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenDocumentExists_ShouldReturnDocumentWithAllIncludesAsync()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("test content");
        string organizationId = "org1";
        string userId = "user1";
        string fileName = "GetByIdAsync_WhenDocumentExists.txt";
        int fileSize = content.Length;

        (Document document, _) = await _repository.GetOrCreateDocumentAsync(content, organizationId, userId, fileName, fileSize, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);

        var page = new Page(document, 1, 1) { Text = "test page" };
        var vector = new EmbeddingVector("test-model", [1.0, 2.0]);

        page.EmbeddingVector = vector;
        page.EmbeddingVectorId = vector.Id;
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        // Act
        Document? result = await _repository.GetByIdAsync(document.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.FileMetadata);
        Page loadedPage = Assert.Single(result.Pages);
        Assert.Equal("test page", loadedPage.Text);
        Assert.NotNull(loadedPage.EmbeddingVector);
        Assert.Equal("test-model", loadedPage.EmbeddingVector.Model);
    }

    [Fact]
    public async Task GetRunInputByJobIdAsync_LeavesOutADocumentExcludedFromTheRun()
    {
        (DocumentInfo kept, DocumentInfo excluded) = await SeedJobWithOneExcludedDocumentAsync("job-reader-input");

        List<DocumentInfo> result = await _repository.GetRunInputByJobIdAsync("job-reader-input");

        DocumentInfo only = Assert.Single(result);
        Assert.Equal(kept.Id, only.Id);
        Assert.DoesNotContain(result, di => di.Id == excluded.Id);
    }

    [Fact]
    public async Task GetByJobIdWithMetadataAsync_KeepsTheExcludedDocument_ForTheReportToShow()
    {
        (_, DocumentInfo excluded) = await SeedJobWithOneExcludedDocumentAsync("job-reader-report");

        List<DocumentInfo> result = await _repository.GetByJobIdWithMetadataAsync("job-reader-report");

        Assert.Equal(2, result.Count);
        DocumentInfo reread = Assert.Single(result, di => di.IsExcludedFromRun);
        Assert.Equal(excluded.Id, reread.Id);
        Assert.Equal("NotExpectedDocument", reread.ExclusionReason);
        Assert.NotNull(reread.Document?.FileMetadata);
    }

    private async Task<(DocumentInfo Kept, DocumentInfo Excluded)> SeedJobWithOneExcludedDocumentAsync(string jobId)
    {
        (Document keptDocument, _) = await _repository.GetOrCreateDocumentAsync(
            Encoding.UTF8.GetBytes($"kept {jobId}"), "org1", "user1", $"kept-{jobId}.txt", 10, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        (Document excludedDocument, _) = await _repository.GetOrCreateDocumentAsync(
            Encoding.UTF8.GetBytes($"excluded {jobId}"), "org1", "user1", $"excluded-{jobId}.txt", 14, Domain.Enums.StorageProvider.Database, MediaType.TextPlain);
        var kept = new DocumentInfo(keptDocument.Id) { Document = keptDocument, JobId = jobId };
        var excluded = new DocumentInfo(excludedDocument.Id) { Document = excludedDocument, JobId = jobId };
        excluded.ExcludeFromRun("NotExpectedDocument", DateTime.UtcNow);
        await _repository.AddDocumentInfoAsync(kept);
        await _repository.AddDocumentInfoAsync(excluded);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return (kept, excluded);
    }
}
