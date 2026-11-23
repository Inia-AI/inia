using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// A stored file streams into and out of the database on both servers: the insert sends the caller's
/// stream as its parameter, and a read holds the row with sequential access and knows the size before
/// the first byte. A read stopped halfway gives its connection back, so the row can still be deleted.
/// </summary>
public abstract class DatabaseFileStreamingOnBothProvidersTests
{
    private const string ORG_ID = "org-file-streaming";
    private const string TEST_DB_PREFIX = "iniaai_file_streaming_";
    private const string GUARD_NAME = "the database file streaming guard";
    private const int LARGE_FILE_BYTES = 1024 * 1024;
    private const int HALF_READ_BYTES = 64 * 1024;

    private readonly string _provider;

    protected DatabaseFileStreamingOnBothProvidersTests(string provider)
    {
        _provider = provider;
    }

    [Fact]
    public async Task ALargeFile_StreamsInAndOut_Whole()
    {
        await WithStorageAsync(async storage =>
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            byte[] content = RandomNumberGenerator.GetBytes(LARGE_FILE_BYTES);

            string key = await storage.UploadAsync(ORG_ID, StorageCategories.Documents, Guid.NewGuid().ToString(), new MemoryStream(content), "application/pdf");

            await using Stream download = await storage.DownloadAsync(ORG_ID, StorageCategories.Documents, key);
            StoredObjectStream stored = Assert.IsType<StoredObjectStream>(download);
            Assert.Equal(LARGE_FILE_BYTES, stored.Length);
            Assert.Equal(SHA256.HashData(content), await SHA256.HashDataAsync(stored, cancellationToken));
        });
    }

    [Fact]
    public async Task AReadStoppedHalfway_GivesItsConnectionBack_AndTheFileCanBeDeleted()
    {
        await WithStorageAsync(async storage =>
        {
            byte[] content = RandomNumberGenerator.GetBytes(HALF_READ_BYTES * 4);
            string key = await storage.UploadAsync(ORG_ID, StorageCategories.Documents, Guid.NewGuid().ToString(), new MemoryStream(content), "application/pdf");

            await using (Stream download = await storage.DownloadAsync(ORG_ID, StorageCategories.Documents, key))
            {
                byte[] firstPart = new byte[HALF_READ_BYTES];
                await download.ReadExactlyAsync(firstPart, TestContext.Current.CancellationToken);
                Assert.Equal(content[..HALF_READ_BYTES], firstPart);
            }

            await storage.DeleteAsync(ORG_ID, StorageCategories.Documents, key);

            _ = await Assert.ThrowsAsync<FileNotFoundException>(
                () => storage.DownloadAsync(ORG_ID, StorageCategories.Documents, key));
        });
    }

    private async Task WithStorageAsync(Func<DatabaseStorageService, Task> test)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> options = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(_provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);

        try
        {
            await using (UserDataDbContext setup = new(options))
            {
                await setup.Database.MigrateAsync(cancellationToken);
            }

            var services = new ServiceCollection();
            _ = services.AddScoped(_ => new UserDataDbContext(options));
            await using ServiceProvider provider = services.BuildServiceProvider();

            await test(new DatabaseStorageService(provider.GetRequiredService<IServiceScopeFactory>()));
        }
        finally
        {
            await using UserDataDbContext cleanup = new(options);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }
}

public sealed class DatabaseFileStreamingOnPostgresTests()
    : DatabaseFileStreamingOnBothProvidersTests(BothProvidersUserDataDatabase.POSTGRESQL);

public sealed class DatabaseFileStreamingOnSqlServerTests()
    : DatabaseFileStreamingOnBothProvidersTests(BothProvidersUserDataDatabase.MSSQL);
