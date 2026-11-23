using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using IniaAi.Infrastructure.Tests.Helpers;
using IniaAi.Testing.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using File = IniaAi.Domain.Entities.Documents.File;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;
using SqliteUserDataSchema = IniaAi.Testing.DependencyInjection.SqliteUserDataSchema;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// Pins that deleting a database-stored blob never reads its bytes: the reapers call this in a
/// loop, and a delete that first loads each blob turns a sweep into a memory spike sized by the
/// data it is throwing away. SQLite with a command interceptor, so the assertion is on the SQL
/// that actually ran.
/// </summary>
public class DatabaseStorageServiceTests : IDisposable
{
    private const string ORG_ID = "org-storage";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly SqlCapturingInterceptor _interceptor = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly DatabaseStorageService _service;

    public DatabaseStorageServiceTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;

        var services = new ServiceCollection();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(_options));
        _serviceProvider = services.BuildServiceProvider();

        _service = new DatabaseStorageService(_serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRowWithoutReadingItsBytes()
    {
        var stored = new File([1, 2, 3]) { OrganizationId = ORG_ID };
        await using (var seed = new SqliteUserDataDbContext(_options))
        {
            _ = await seed.Files.AddAsync(stored, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _interceptor.Commands.Clear();
        await _service.DeleteAsync(ORG_ID, StorageCategories.AgentSessionBundles, stored.Id);

        string deleteCommand = Assert.Single(_interceptor.Commands);
        Assert.StartsWith("DELETE", deleteCommand);
        Assert.DoesNotContain("Bytes", deleteCommand);

        await using var check = new SqliteUserDataDbContext(_options);
        Assert.False(await check.Files.AnyAsync(
            file => file.Id == stored.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_AnotherOrganizationsKey_DeletesNothing()
    {
        var stored = new File([9, 9, 9]) { OrganizationId = ORG_ID };
        await using (var seed = new SqliteUserDataDbContext(_options))
        {
            _ = await seed.Files.AddAsync(stored, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _service.DeleteAsync("some-other-org", StorageCategories.AgentSessionBundles, stored.Id);

        await using var check = new SqliteUserDataDbContext(_options);
        Assert.True(await check.Files.AnyAsync(
            file => file.Id == stored.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RowIsAlreadyGone_IsAQuietNoOp()
    {
        _interceptor.Commands.Clear();

        await _service.DeleteAsync(ORG_ID, StorageCategories.AgentSessionBundles, "no-such-file");

        string deleteCommand = Assert.Single(_interceptor.Commands);
        Assert.StartsWith("DELETE", deleteCommand);
    }

    [Fact]
    public async Task UploadAsync_ThenDownloadAsync_ReturnsTheBytes_AsAStreamThatKnowsItsSize()
    {
        byte[] content = Enumerable.Range(0, 70_000).Select(index => (byte)(index % 251)).ToArray();

        string key = await _service.UploadAsync(ORG_ID, StorageCategories.Documents, "file-round-trip", new MemoryStream(content), "application/pdf");

        await using Stream download = await _service.DownloadAsync(ORG_ID, StorageCategories.Documents, key);
        StoredObjectStream stored = Assert.IsType<StoredObjectStream>(download);
        Assert.Equal(content.Length, stored.Length);
        Assert.False(stored.CanSeek);
        Assert.Equal(content, await StoredObjectStream.ReadAllAsync(stored, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UploadAsync_AStreamThatCannotSeek_IsStoredWhole()
    {
        byte[] content = Enumerable.Range(0, 20_000).Select(index => (byte)(index % 13)).ToArray();

        string key = await _service.UploadAsync(ORG_ID, StorageCategories.Documents, "file-forward-only", new ForwardOnlyStream(content), "application/pdf");

        await using var check = new SqliteUserDataDbContext(_options);
        File stored = await check.Files.SingleAsync(file => file.Id == key, TestContext.Current.CancellationToken);
        Assert.Equal(content, stored.Bytes);
        Assert.Equal(ORG_ID, stored.OrganizationId);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task DownloadAsync_AFileSavedThroughTheModel_CopiesItsBytes()
    {
        byte[] content = [.. "Balance sheet of the customer's company."u8];
        var stored = new File(content) { OrganizationId = ORG_ID };
        await using (var seed = new SqliteUserDataDbContext(_options))
        {
            _ = await seed.Files.AddAsync(stored, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using Stream download = await _service.DownloadAsync(ORG_ID, StorageCategories.Documents, stored.Id);
        using var copy = new MemoryStream();
        await download.CopyToAsync(copy, 81_920, TestContext.Current.CancellationToken);

        Assert.Equal(content.Length, download.Length);
        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task DownloadAsync_AMissingKey_ThrowsFileNotFound()
    {
        _ = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.DownloadAsync(ORG_ID, StorageCategories.Documents, "no-such-file"));
    }

    private sealed class SqlCapturingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
