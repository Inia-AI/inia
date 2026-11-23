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
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Storage;

/// <summary>
/// A deployment that moves to S3, end to end on both servers and a real S3-compatible store: the
/// database's files move into the bucket under their organization's prefix, the database copies go,
/// and a day later the sweep removes the objects no row points at while keeping the rest.
/// </summary>
public abstract class FileStoreMaintenanceOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_file_store_";
    private const string GUARD_NAME = "the file store maintenance guard";

    private readonly string _provider;

    protected FileStoreMaintenanceOnBothProvidersTests(string provider)
    {
        _provider = provider;
    }

    [Fact]
    public async Task TheDatabasesFiles_MoveIntoTheBucket_AndTheSweepRemovesOnlyWhatNoRowPointsAt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ObjectStoreHarness store = await ObjectStoreHarness.StartOrSkipAsync(cancellationToken);
        DbContextOptions<UserDataDbContext> options = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(_provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);
        var clock = new SettableClock(DateTimeOffset.UtcNow);

        try
        {
            await using (UserDataDbContext setup = new(options))
            {
                await setup.Database.MigrateAsync(cancellationToken);
            }

            await using Microsoft.Extensions.DependencyInjection.ServiceProvider services = Services(options, store, clock);
            var organization = new Organization("Moving organization");
            await using (UserDataDbContext seed = new(options))
            {
                _ = seed.Organizations.Add(organization);
                _ = await seed.SaveChangesAsync(cancellationToken);
            }

            (string Id, string Key) kept = await SeedAsync(services, options, organization.Id, "kept.pdf", [1, 2, 3], DateTime.UtcNow.AddMinutes(-3));
            (string Id, string Key) removedLater = await SeedAsync(services, options, organization.Id, "removed.pdf", [4, 5], DateTime.UtcNow.AddMinutes(-2));
            (string Id, string Key) template = await SeedAsync(services, options, organization.Id, "layout.xlsx", [6], DateTime.UtcNow.AddMinutes(-1));
            await using (UserDataDbContext seed = new(options))
            {
                _ = seed.Json2XlsxTemplates.Add(new Json2XlsxTemplate { OrganizationId = organization.Id, ConversionType = "test", FileMetadataId = template.Id, OriginalFileName = "layout.xlsx" });
                _ = await seed.SaveChangesAsync(cancellationToken);
            }

            FileMigrationTally tally = await new FileStorageMigrationService(
                services,
                NullLogger<FileStorageMigrationService>.Instance,
                services.GetRequiredService<IOptions<FileStorageSettings>>(),
                TimeSpan.Zero).MigrateAsync(cancellationToken);

            Assert.Equal(3, tally.Migrated);
            await using (UserDataDbContext check = new(options))
            {
                Assert.All(await check.FileMetadata.ToListAsync(cancellationToken), file => Assert.Equal(StorageProvider.S3, file.StorageProvider));
                Assert.False(await check.Files.IgnoreQueryFilters().AnyAsync(cancellationToken));
            }

            Assert.Equal(
                new[] { kept, removedLater, template }.Select(file => $"{organization.Id}/{file.Key}").Order(),
                (await store.ObjectKeysAsync()).Order());

            _ = await store.Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = store.Bucket,
                Key = $"{organization.Id}/never-had-a-row",
                ContentBody = "orphan",
            }, cancellationToken);
            await using (UserDataDbContext remove = new(options))
            {
                _ = await remove.FileMetadata.Where(file => file.Id == removedLater.Id).ExecuteDeleteAsync(cancellationToken);
            }

            clock.Advance(StoredObjectSweep.Grace + TimeSpan.FromHours(1));
            (int _, int bucket) = await services.GetRequiredService<StoredObjectSweep>().SweepAsync(cancellationToken);

            Assert.Equal(2, bucket);
            Assert.Equal(
                new[] { kept, template }.Select(file => $"{organization.Id}/{file.Key}").Order(),
                (await store.ObjectKeysAsync()).Order());
        }
        finally
        {
            await using UserDataDbContext cleanup = new(options);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider Services(DbContextOptions<UserDataDbContext> options, ObjectStoreHarness store, SettableClock clock)
    {
        var services = new ServiceCollection();
        _ = services.AddScoped(_ => new UserDataDbContext(options));
        _ = services.AddFileStores(FileStorageProviders.S3);
        _ = services.AddKeyedSingleton(FileStorageProviders.S3, (_, _) => store.Store());
        _ = services.AddKeyedSingleton<IFileStorageService>(FileStorageProviders.S3, (provider, key) => provider.GetRequiredKeyedService<ObjectStorageService>(key));
        _ = services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        _ = services.AddSingleton<TimeProvider>(clock);
        _ = services.AddSingleton<StoredObjectSweep>();
        return services.BuildServiceProvider();
    }

    private static async Task<(string Id, string Key)> SeedAsync(IServiceProvider services, DbContextOptions<UserDataDbContext> options, string organizationId, string name, byte[] bytes, DateTime createdAt)
    {
        string key = Guid.NewGuid().ToString();
        await using (UserDataDbContext context = new(options))
        {
            _ = context.FileMetadata.Add(new FileMetadata(organizationId, name, bytes.Length, StorageProvider.Database, key) { CreatedAt = createdAt });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IFileStorageService database = services.GetRequiredKeyedService<IFileStorageService>(FileStorageProviders.DATABASE);
        _ = await database.UploadAsync(organizationId, StorageCategories.Documents, key, new MemoryStream(bytes), "application/octet-stream");

        await using UserDataDbContext read = new(options);
        return (await read.FileMetadata.Where(file => file.ExternalStoragePath == key).Select(file => file.Id).SingleAsync(TestContext.Current.CancellationToken), key);
    }
}

public sealed class FileStoreMaintenanceOnPostgresTests()
    : FileStoreMaintenanceOnBothProvidersTests(BothProvidersUserDataDatabase.POSTGRESQL);

public sealed class FileStoreMaintenanceOnSqlServerTests()
    : FileStoreMaintenanceOnBothProvidersTests(BothProvidersUserDataDatabase.MSSQL);
