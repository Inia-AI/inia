using IniaAi.Common.Enums;
using IniaAi.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Documents;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Application.UnitTests.Documents;

/// <summary>
/// The in-memory UserData store and a real (unbounded, drainable) processing queue that
/// document-side handler tests share.
/// </summary>
public abstract class DocumentTestHost : IDisposable
{
    protected const string ORG_ID = "org-test";
    protected const string OTHER_ORG_ID = "org-other";
    protected const string USER_ID = "user-test";

    protected readonly ServiceProvider _serviceProvider;
    protected readonly UserDataDbContext _context;
    protected readonly IUserDataUnitOfWork _userData;
    protected readonly DocumentProcessingQueue _queue = new();

    protected DocumentTestHost()
    {
        ServiceCollection services = new();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _userData = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    protected async Task<Document> SeedDocumentAsync(string organizationId, string fileName, MediaType? mediaType = null, Action<Document>? configure = null)
    {
        FileMetadata fileMetadata = new(organizationId, fileName, sizeBytes: 1024, storageProvider: StorageProvider.Database, externalStoragePath: Guid.NewGuid().ToString());
        Document document = new(fileMetadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName))) { OrganizationId = organizationId, Name = Path.GetFileNameWithoutExtension(fileName) };
        if (mediaType is not null)
        {
            document.MediaType = mediaType.Value;
        }

        configure?.Invoke(document);
        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return document;
    }

    protected async Task<Document> SeedDocumentWithPagesAsync(string organizationId, string fileName, params Action<Page>[] pages)
    {
        Document document = await SeedDocumentAsync(organizationId, fileName);
        Document tracked = _context.Documents.Single(d => d.Id == document.Id);
        for (int i = 0; i < pages.Length; i++)
        {
            Page page = new(tracked, i + 1, i + 1) { Status = PageStatus.ProcessedSuccessfully };
            pages[i](page);
        }

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return document;
    }

    protected Document Reload(string documentId)
    {
        _context.ChangeTracker.Clear();
        return _context.Documents.Single(d => d.Id == documentId);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
