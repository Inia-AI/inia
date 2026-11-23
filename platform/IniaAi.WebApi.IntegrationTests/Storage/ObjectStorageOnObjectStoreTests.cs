using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Fixtures;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace IniaAi.WebApi.IntegrationTests.Storage;

/// <summary>
/// The S3 store against a real S3-compatible server: a file lands under its organization's prefix,
/// streams back with its size known before the first byte, and is gone after a delete. A stream that
/// cannot seek is stored whole: one of known size, as a move between stores sends it, straight
/// through, and any other after the storage manager buffers it.
/// </summary>
public sealed class ObjectStorageOnObjectStoreTests
{
    private const string ORG_ID = "org-object-store";
    private const int LARGE_FILE_BYTES = 12 * 1024 * 1024;

    [Fact]
    public async Task AFile_LandsUnderItsOrganizationsPrefix_StreamsBack_AndIsGoneAfterADelete()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ObjectStoreHarness store = await ObjectStoreHarness.StartOrSkipAsync(cancellationToken);
        byte[] content = RandomNumberGenerator.GetBytes(LARGE_FILE_BYTES);
        string key = Guid.NewGuid().ToString();

        string stored = await store.Store().UploadAsync(ORG_ID, StorageCategories.Documents, key, new MemoryStream(content), "application/pdf");

        Assert.Equal(key, stored);
        Assert.Equal([$"{ORG_ID}/{key}"], await store.ObjectKeysAsync());
        await using (Stream download = await store.Store().DownloadAsync(ORG_ID, StorageCategories.Documents, key))
        {
            StoredObjectStream streamed = Assert.IsType<StoredObjectStream>(download);
            Assert.Equal(LARGE_FILE_BYTES, streamed.Length);
            Assert.Equal(SHA256.HashData(content), await SHA256.HashDataAsync(streamed, cancellationToken));
        }

        await store.Store().DeleteAsync(ORG_ID, StorageCategories.Json2XlsxTemplates, key);

        Assert.Empty(await store.ObjectKeysAsync());
        _ = await Assert.ThrowsAsync<FileNotFoundException>(() => store.Store().DownloadAsync(ORG_ID, StorageCategories.Documents, key));
    }

    [Fact]
    public async Task AStreamOfKnownSizeThatCannotSeek_IsStoredWhole()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ObjectStoreHarness store = await ObjectStoreHarness.StartOrSkipAsync(cancellationToken);
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string key = Guid.NewGuid().ToString();

        _ = await store.Store().UploadAsync(ORG_ID, StorageCategories.Documents, key, new StoredObjectStream(new ForwardOnlyStream(content), content.Length), "application/pdf");

        await using Stream download = await store.Store().DownloadAsync(ORG_ID, StorageCategories.Documents, key);
        Assert.Equal(SHA256.HashData(content), await SHA256.HashDataAsync(download, cancellationToken));
    }

    [Fact]
    public async Task AStreamOfUnknownSize_IsBufferedByTheManager_AndStoredWhole()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ObjectStoreHarness store = await ObjectStoreHarness.StartOrSkipAsync(cancellationToken);
        var manager = new FileStorageManager(new FixedFileStorageResolver(store.Store()), Options.Create(new FileStorageSettings()));
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string key = Guid.NewGuid().ToString();

        _ = await manager.StoreFileAsync(FileStorageProviders.S3, ORG_ID, StorageCategories.Documents, key, new ForwardOnlyStream(content), "application/pdf");

        Assert.Equal(content, await manager.RetrieveFileBytesAsync(FileStorageProviders.S3, ORG_ID, StorageCategories.Documents, key));
    }

    [Fact]
    public async Task TheListing_NamesEachObjectsOrganizationAndKey()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ObjectStoreHarness store = await ObjectStoreHarness.StartOrSkipAsync(cancellationToken);
        string key = Guid.NewGuid().ToString();
        _ = await store.Store().UploadAsync(ORG_ID, StorageCategories.Documents, key, new MemoryStream([1, 2, 3]), "application/pdf");

        StoredObjectEntry only = Assert.Single(await store.Store().ListAsync(cancellationToken).ToListAsync(cancellationToken));
        Assert.Equal(key, only.Key);
        Assert.Equal($"{ORG_ID}/{key}", only.ObjectKey);
        Assert.NotNull(only.LastModified);
    }
}
