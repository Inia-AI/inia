using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Moving the database's files into the default store: each file keeps its key, its row points at
/// the new store, and the database copy is gone, whatever letter case the settings name the store
/// in. A file that fails, has no storage key, or belongs to an organization whose override names
/// the database, is passed over and the rest still move. A row that names the store while its bytes
/// sit in the database gets them there, and the database copy goes. The
/// database store is real (SQLite); the target store stands in for S3, whose own round trip is
/// tested against a real S3-compatible server.
/// </summary>
public sealed class FileStorageMigrationServiceTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly TestFileStorageService _target = new();
    private readonly Organization _organization = new("Migration organization");

    public FileStorageMigrationServiceTests()
        : this("S3")
    {
    }

    private FileStorageMigrationServiceTests(string defaultProvider, bool keptInTheDatabase = false)
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _ = services.AddFileStores(defaultProvider);
        if (keptInTheDatabase)
        {
            _ = services.AddSingleton(Options.Create(new FileStorageSettings
            {
                DefaultProvider = defaultProvider,
                TenantOverrides = new Dictionary<string, string> { [_organization.Id] = FileStorageProviders.DATABASE },
            }));
        }

        _ = services.AddKeyedSingleton<IFileStorageService>(FileStorageProviders.S3, (_, _) => _target);
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.Organizations.Add(_organization);
        _ = context.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public async Task EveryFile_MovesUnderItsKey_ItsRowPointsAtTheNewStore_AndTheDatabaseCopyIsGone()
    {
        byte[] report = [1, 2, 3, 4];
        byte[] template = [9, 8, 7];
        string reportId = await SeedDocumentAsync("report.pdf", report);
        string templateId = await SeedTemplateAsync("layout.xlsx", template);

        FileMigrationTally tally = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, tally.Migrated);
        Assert.Equal(0, tally.Errors);
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        FileMetadata movedReport = await context.FileMetadata.SingleAsync(file => file.Id == reportId, TestContext.Current.CancellationToken);
        FileMetadata movedTemplate = await context.FileMetadata.SingleAsync(file => file.Id == templateId, TestContext.Current.CancellationToken);
        Assert.Equal(StorageProvider.S3, movedReport.StorageProvider);
        Assert.Equal(StorageProvider.S3, movedTemplate.StorageProvider);
        Assert.Equal(report, _target.GetTestFile(_organization.Id, StorageCategories.Documents, movedReport.ExternalStoragePath!));
        Assert.Equal(template, _target.GetTestFile(_organization.Id, StorageCategories.Documents, movedTemplate.ExternalStoragePath!));
        Assert.False(await context.Files.IgnoreQueryFilters().AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileThatFails_IsPassedOver_AndTheOthersStillMove()
    {
        string brokenId = await SeedDocumentAsync("broken.pdf", [5, 5, 5]);
        string goodId = await SeedDocumentAsync("good.pdf", [6, 6, 6]);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            string brokenKey = (await context.FileMetadata.SingleAsync(file => file.Id == brokenId, TestContext.Current.CancellationToken)).ExternalStoragePath!;
            _ = await context.Files.Where(file => file.Id == brokenKey).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        FileMigrationTally tally = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tally.Migrated);
        Assert.Equal(1, tally.Errors);
        using IServiceScope check = _provider.CreateScope();
        UserDataDbContext checkContext = check.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Equal(StorageProvider.Database, (await checkContext.FileMetadata.SingleAsync(file => file.Id == brokenId, TestContext.Current.CancellationToken)).StorageProvider);
        Assert.Equal(StorageProvider.S3, (await checkContext.FileMetadata.SingleAsync(file => file.Id == goodId, TestContext.Current.CancellationToken)).StorageProvider);
    }

    [Fact]
    public async Task AFileWithNoStorageKey_IsPassedOver_AndTheOthersStillMove()
    {
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.FileMetadata.Add(new FileMetadata(_organization.Id, "keyless.pdf", 3, StorageProvider.Database) { CreatedAt = DateTime.UtcNow.AddDays(-1) });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        string goodId = await SeedDocumentAsync("good.pdf", [6, 6, 6]);

        FileMigrationTally tally = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tally.Migrated);
        Assert.Equal(1, tally.Skipped);
        using IServiceScope check = _provider.CreateScope();
        UserDataDbContext checkContext = check.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Equal(StorageProvider.S3, (await checkContext.FileMetadata.SingleAsync(file => file.Id == goodId, TestContext.Current.CancellationToken)).StorageProvider);
    }

    [Fact]
    public async Task AStoreNamedInLowerCase_IsTheStoreTheRowRecords()
    {
        using var lowerCase = new FileStorageMigrationServiceTests("s3");
        string reportId = await lowerCase.SeedDocumentAsync("report.pdf", [1, 2, 3]);

        FileMigrationTally tally = await lowerCase.NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tally.Migrated);
        using IServiceScope scope = lowerCase._provider.CreateScope();
        FileMetadata moved = await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().FileMetadata
            .SingleAsync(file => file.Id == reportId, TestContext.Current.CancellationToken);
        Assert.Equal(StorageProvider.S3, moved.StorageProvider);
        Assert.Equal([1, 2, 3], lowerCase._target.GetTestFile(lowerCase._organization.Id, StorageCategories.Documents, moved.ExternalStoragePath!));
    }

    [Fact]
    public async Task ARowRecordedInTheStore_WithItsBytesInTheDatabase_GetsThemInTheStore()
    {
        byte[] bytes = [4, 4, 4];
        FileMetadata stray = await SeedStrayAsync("written-by-an-older-release.pdf", bytes);

        FileMigrationTally tally = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tally.CopiesSettled);
        Assert.Equal(bytes, _target.GetTestFile(_organization.Id, StorageCategories.Documents, stray.ExternalStoragePath!));
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.False(await context.Files.IgnoreQueryFilters().AnyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(StorageProvider.S3, (await context.FileMetadata.SingleAsync(file => file.Id == stray.Id, TestContext.Current.CancellationToken)).StorageProvider);
    }

    [Fact]
    public async Task ADatabaseCopyOfAFileTheStoreHolds_Goes_AndTheStoresCopyStays()
    {
        byte[] inTheStore = [7, 7];
        FileMetadata kept = await SeedStrayAsync("moved-and-kept.pdf", [1, 2, 3]);
        _target.AddTestFile(_organization.Id, StorageCategories.Documents, kept.ExternalStoragePath!, inTheStore);

        FileMigrationTally tally = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, tally.CopiesSettled);
        Assert.Equal(inTheStore, _target.GetTestFile(_organization.Id, StorageCategories.Documents, kept.ExternalStoragePath!));
        using IServiceScope scope = _provider.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().Files.IgnoreQueryFilters().AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADatabaseCopyThatADatabaseRowAlsoNames_Stays()
    {
        FileMetadata stray = await SeedStrayAsync("shared.pdf", [3, 3, 3]);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.FileMetadata.Add(new FileMetadata(_organization.Id, "shared-copy.pdf", 3, StorageProvider.Database, stray.ExternalStoragePath) { IsDeleted = true });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _ = await NewService().MigrateAsync(TestContext.Current.CancellationToken);

        using IServiceScope check = _provider.CreateScope();
        Assert.True(await check.ServiceProvider.GetRequiredService<UserDataDbContext>().Files.IgnoreQueryFilters()
            .AnyAsync(file => file.Id == stray.ExternalStoragePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOrganizationWhoseOverrideNamesTheDatabase_KeepsItsFilesThere()
    {
        using var pinned = new FileStorageMigrationServiceTests("S3", keptInTheDatabase: true);
        string reportId = await pinned.SeedDocumentAsync("report.pdf", [1, 2, 3]);

        FileMigrationTally tally = await pinned.NewService().MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, tally.Migrated);
        Assert.Equal(1, tally.Skipped);
        using IServiceScope scope = pinned._provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        FileMetadata kept = await context.FileMetadata.SingleAsync(file => file.Id == reportId, TestContext.Current.CancellationToken);
        Assert.Equal(StorageProvider.Database, kept.StorageProvider);
        Assert.True(await context.Files.AnyAsync(file => file.Id == kept.ExternalStoragePath, TestContext.Current.CancellationToken));
    }

    private FileStorageMigrationService NewService()
    {
        return new FileStorageMigrationService(
            _provider,
            NullLogger<FileStorageMigrationService>.Instance,
            _provider.GetRequiredService<IOptions<FileStorageSettings>>(),
            TimeSpan.Zero);
    }

    private async Task<string> SeedDocumentAsync(string name, byte[] bytes)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        FileMetadata metadata = await SeedFileAsync(scope, name, bytes);
        _ = context.Documents.Add(new Document(metadata, MediaType.ApplicationPdf) { OrganizationId = _organization.Id });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return metadata.Id;
    }

    private async Task<string> SeedTemplateAsync(string name, byte[] bytes)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        FileMetadata metadata = await SeedFileAsync(scope, name, bytes);
        _ = context.Json2XlsxTemplates.Add(new Json2XlsxTemplate
        {
            OrganizationId = _organization.Id,
            ConversionType = "test",
            FileMetadataId = metadata.Id,
            OriginalFileName = name,
        });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return metadata.Id;
    }

    /// <summary>A row an older release recorded as S3 while it wrote the bytes to the database.</summary>
    private async Task<FileMetadata> SeedStrayAsync(string name, byte[] bytes)
    {
        using IServiceScope scope = _provider.CreateScope();
        return await SeedFileAsync(scope, name, bytes, StorageProvider.S3);
    }

    private async Task<FileMetadata> SeedFileAsync(IServiceScope scope, string name, byte[] bytes, StorageProvider recorded = StorageProvider.Database)
    {
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        string key = Guid.NewGuid().ToString();
        var metadata = new FileMetadata(_organization.Id, name, bytes.Length, recorded, key);
        _ = context.FileMetadata.Add(metadata);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        IFileStorageService database = scope.ServiceProvider.GetRequiredKeyedService<IFileStorageService>(FileStorageProviders.DATABASE);
        _ = await database.UploadAsync(_organization.Id, StorageCategories.Documents, key, new MemoryStream(bytes), "application/octet-stream");
        return metadata;
    }
}
