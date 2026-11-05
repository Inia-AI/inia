using ErrorOr;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Documents.BulkDeleteDocuments;

public class BulkDeleteDocumentsCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly BulkDeleteDocumentsCommandHandler _handler;

    public BulkDeleteDocumentsCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddTestFileStorage();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        IFileStorageManager fileStorageManager = _serviceProvider.GetRequiredService<IFileStorageManager>();

        _handler = new BulkDeleteDocumentsCommandHandler(
            unitOfWork,
            fileStorageManager,
            NullLogger<BulkDeleteDocumentsCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_MixOfValidAndInvalidIds_ReturnsPerIdOutcomes()
    {
        Document owned = await SeedDocumentAsync(ORG_ID, "Owned");
        Document crossOrg = await SeedDocumentAsync(OTHER_ORG_ID, "CrossOrg");
        const string missingId = "missing-doc";

        var command = new BulkDeleteDocumentsCommand(
            ORG_ID,
            USER_ID,
            [owned.Id, crossOrg.Id, missingId]);

        ErrorOr<BulkDeleteDocumentsResultDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Deleted);
        Assert.Equal(owned.Id, result.Value.Deleted[0]);
        Assert.Equal(2, result.Value.Failed.Count);
        Assert.True(result.Value.Failed.ContainsKey(crossOrg.Id));
        Assert.True(result.Value.Failed.ContainsKey(missingId));

        Document? reloadedOwned = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(
            d => d.Id == owned.Id, TestContext.Current.CancellationToken);
        Assert.Null(reloadedOwned);

        Document? reloadedCrossOrg = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(
            d => d.Id == crossOrg.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloadedCrossOrg);
    }

    [Fact]
    public async Task Handle_DeduplicatesRepeatedIds()
    {
        Document doc = await SeedDocumentAsync(ORG_ID, "Dedup");

        var command = new BulkDeleteDocumentsCommand(
            ORG_ID,
            USER_ID,
            [doc.Id, doc.Id, doc.Id]);

        ErrorOr<BulkDeleteDocumentsResultDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Deleted);
    }

    private async Task<Document> SeedDocumentAsync(string organizationId, string name)
    {
        var fileMetadata = new FileMetadata(
            organizationId,
            $"{name}.pdf",
            sizeBytes: 1024,
            storageProvider: Domain.Enums.StorageProvider.Database,
            externalStoragePath: $"{Guid.NewGuid()}.pdf");
        _ = await _context.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);

        var document = new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            OrganizationId = organizationId,
            Name = name,
        };
        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return document;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
