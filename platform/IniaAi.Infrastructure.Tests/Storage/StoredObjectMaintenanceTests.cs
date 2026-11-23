using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Storage;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// A file released after its rows are gone whose delete fails is recorded and retried on a growing
/// wait until it deletes; a plain delete that fails records nothing. The sweep removes
/// a database copy that no row points at once it is a day old, in batches, and keeps every copy a
/// file's row or an agent session bundle still points at, whatever store the row names and deleted
/// rows included, as well as a young one whose row may not be saved yet.
/// </summary>
public sealed class StoredObjectMaintenanceTests : IDisposable
{
    private const string FLAKY_STORE = "S3";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly SettableClock _clock = new(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
    private readonly FlakyStore _flaky = new();
    private readonly Organization _organization = new("Maintenance organization");

    public StoredObjectMaintenanceTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _ = services.AddFileStores(FileStorageProviders.DATABASE);
        _ = services.AddKeyedSingleton<IFileStorageService>(FLAKY_STORE, (_, _) => _flaky);
        _ = services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        _ = services.AddSingleton<TimeProvider>(_clock);
        _ = services.AddSingleton<StoredObjectDeletionRetries>();
        _ = services.AddSingleton<IStoredObjectDeletionRetries>(provider => provider.GetRequiredService<StoredObjectDeletionRetries>());
        _ = services.AddSingleton<FileStorageManager>();
        _ = services.AddSingleton<StoredObjectSweep>();
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
    public async Task AFailedRelease_IsRecorded_ThenRetriedOnAGrowingWait_UntilItDeletes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FileStorageManager manager = _provider.GetRequiredService<FileStorageManager>();
        StoredObjectDeletionRetries retries = _provider.GetRequiredService<StoredObjectDeletionRetries>();
        _flaky.FailuresLeft = 2;

        _ = Assert.IsType<IOException>(await manager.ReleaseFileAsync(FLAKY_STORE, _organization.Id, StorageCategories.Documents, "file-key"));

        StoredObjectDeletion recorded = await SingleDeletionAsync();
        Assert.Equal(1, recorded.Attempts);
        Assert.Equal("file-key", recorded.StorageKey);
        Assert.Equal(_clock.UtcNow.AddMinutes(15), recorded.NextAttemptAt);

        Assert.Equal(0, await retries.RetryDueAsync(cancellationToken));
        Assert.Empty(_flaky.Deleted);

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(0, await retries.RetryDueAsync(cancellationToken));
        StoredObjectDeletion second = await SingleDeletionAsync();
        Assert.Equal(2, second.Attempts);
        Assert.Equal(_clock.UtcNow.AddMinutes(30), second.NextAttemptAt);

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await retries.RetryDueAsync(cancellationToken));
        Assert.Equal(["file-key"], _flaky.Deleted);
        using IServiceScope scope = _provider.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().StoredObjectDeletions.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task APlainDeleteThatFails_Throws_AndRecordsNothing()
    {
        FileStorageManager manager = _provider.GetRequiredService<FileStorageManager>();
        _flaky.FailuresLeft = 1;

        _ = await Assert.ThrowsAsync<IOException>(() => manager.DeleteFileAsync(FLAKY_STORE, _organization.Id, StorageCategories.Documents, "row-kept"));

        using IServiceScope scope = _provider.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().StoredObjectDeletions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheSweep_RemovesOnlyOldDatabaseCopiesThatNoRowPointsAt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DateTime old = _clock.UtcNow.AddDays(-3);
        string orphan = await SeedCopyAsync(old);
        string young = await SeedCopyAsync(_clock.UtcNow.AddHours(-2));
        string ofAFile = await SeedCopyAsync(old);
        string ofADeletedFile = await SeedCopyAsync(old);
        string ofARowNamingAnotherStore = await SeedCopyAsync(old);
        string ofABundle = await SeedCopyAsync(old);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.FileMetadata.Add(new FileMetadata(_organization.Id, "kept.pdf", 3, StorageProvider.Database, ofAFile));
            _ = context.FileMetadata.Add(new FileMetadata(_organization.Id, "deleted.pdf", 3, StorageProvider.Database, ofADeletedFile) { IsDeleted = true, DeletedAt = old });
            // An older release that lacked the S3 store recorded S3 and wrote the bytes here.
            _ = context.FileMetadata.Add(new FileMetadata(_organization.Id, "recorded-as-s3.pdf", 3, StorageProvider.S3, ofARowNamingAnotherStore));
            // The bundle's block needs a section and the section a workflow; only the bundle's key matters here.
            _ = await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", cancellationToken);
            var section = new Section { Id = "sec-sweep", WorkflowId = "wf-sweep" };
            _ = context.Add(section);
            _ = context.Add(new Block(BlockTypes.Agent, section) { Id = "block-sweep" });
            _ = context.AgentRunSessionBundles.Add(new AgentRunSessionBundle
            {
                OrganizationId = _organization.Id,
                BlockId = "block-sweep",
                Round = 1,
                RunExecutionId = "run-sweep",
                Attempt = 1,
                Segment = AgentRunCheckpoint.TERMINAL_SEGMENT,
                Sha256 = new string('b', 64),
                SizeBytes = 3,
                StorageProvider = FileStorageProviders.DATABASE,
                StorageKey = ofABundle,
            });
            _ = await context.SaveChangesAsync(cancellationToken);
        }

        (int database, int bucket) = await _provider.GetRequiredService<StoredObjectSweep>().SweepAsync(cancellationToken);

        Assert.Equal(1, database);
        Assert.Equal(0, bucket);
        using IServiceScope check = _provider.CreateScope();
        List<string> left = await check.ServiceProvider.GetRequiredService<UserDataDbContext>().Files.IgnoreQueryFilters()
            .Select(file => file.Id).ToListAsync(cancellationToken);
        Assert.Equal(new[] { young, ofAFile, ofADeletedFile, ofARowNamingAnotherStore, ofABundle }.Order(), left.Order());
        Assert.DoesNotContain(orphan, left);
    }

    [Fact]
    public async Task TheSweep_RemovesMoreOrphansThanOneBatchHolds()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DateTime old = _clock.UtcNow.AddDays(-3);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            context.Files.AddRange(Enumerable.Range(0, StoredObjectSweep.BATCH_SIZE + 1)
                .Select(_ => new File([1]) { OrganizationId = _organization.Id, CreatedAt = old }));
            _ = await context.SaveChangesAsync(cancellationToken);
        }

        (int database, int _) = await _provider.GetRequiredService<StoredObjectSweep>().SweepAsync(cancellationToken);

        Assert.Equal(StoredObjectSweep.BATCH_SIZE + 1, database);
        using IServiceScope check = _provider.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<UserDataDbContext>().Files.IgnoreQueryFilters().AnyAsync(cancellationToken));
    }

    private async Task<StoredObjectDeletion> SingleDeletionAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().StoredObjectDeletions
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> SeedCopyAsync(DateTime createdAt)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var copy = new File([1, 2, 3]) { OrganizationId = _organization.Id, CreatedAt = createdAt };
        _ = context.Files.Add(copy);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return copy.Id;
    }

    private sealed class FlakyStore : IFileStorageService
    {
        public int FailuresLeft { get; set; }

        public List<string> Deleted { get; } = [];

        public Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
        {
            return Task.FromResult(key);
        }

        public Task<Stream> DownloadAsync(string organizationId, string category, string key)
        {
            throw new FileNotFoundException(key);
        }

        public Task DeleteAsync(string organizationId, string category, string key)
        {
            if (FailuresLeft > 0)
            {
                FailuresLeft--;
                throw new IOException("The store did not answer.");
            }

            Deleted.Add(key);
            return Task.CompletedTask;
        }
    }
}
