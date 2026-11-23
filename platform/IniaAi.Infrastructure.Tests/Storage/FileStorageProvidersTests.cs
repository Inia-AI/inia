using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// A deployment whose default store, or an organization's override, names a store it does not set
/// up stops at startup, and a file recorded in a store the deployment lacks fails its read with that
/// reason. Before, both quietly used the database: the file's row named one store while its bytes
/// went to another.
/// </summary>
public class FileStorageProvidersTests
{
    [Theory]
    [InlineData("Database")]
    [InlineData("")]
    public void TheDatabaseDefault_NeedsNothingElse(string defaultProvider)
    {
        FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings { DefaultProvider = defaultProvider });
    }

    [Fact]
    public void S3_WithItsBucket_Starts()
    {
        FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings
        {
            DefaultProvider = "S3",
            S3 = new S3StorageSettings { BucketName = "iniaai-files" },
        });
    }

    [Theory]
    [InlineData("Minio")]
    [InlineData("MinIO")]
    [InlineData("minio")]
    public void MinIO_WithItsEndpoint_StartsUnderAnySpelling(string defaultProvider)
    {
        FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings
        {
            DefaultProvider = defaultProvider,
            MinIO = new MinioStorageSettings { Endpoint = "minio.internal:9000" },
        });
    }

    [Theory]
    [InlineData("S3", StorageProvider.S3)]
    [InlineData("s3", StorageProvider.S3)]
    [InlineData("MinIO", StorageProvider.MinIO)]
    [InlineData("minio", StorageProvider.MinIO)]
    [InlineData("database", StorageProvider.Database)]
    [InlineData("NotDefined", StorageProvider.Database)]
    [InlineData("", StorageProvider.Database)]
    public void TheManagerAndTheResolver_ReadAStoreNameInAnyCase_AsTheSameStore(string name, StorageProvider store)
    {
        var manager = new FileStorageManager(new FixedFileStorageResolver(new ThrowingFileStorageService()), Options.Create(new FileStorageSettings()));

        Assert.Equal(store, manager.GetStorageProviderEnum(name));
        Assert.Equal(FileStorageProviders.NameOf(store), FileStorageProviders.Normalize(name));
    }

    [Fact]
    public void AStoreNameThePlatformDoesNotKnow_IsNoStoreAtAll()
    {
        var manager = new FileStorageManager(new FixedFileStorageResolver(new ThrowingFileStorageService()), Options.Create(new FileStorageSettings()));

        Assert.Null(FileStorageProviders.Parse("AzureBlob"));
        _ = Assert.Throws<InvalidOperationException>(() => manager.GetStorageProviderEnum("AzureBlob"));
    }

    [Fact]
    public void S3_WithoutABucket_StopsStartup_NamingTheSetting()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings { DefaultProvider = "S3", S3 = new S3StorageSettings() }));

        Assert.Contains("FileStorage:S3:BucketName", refused.Message);
    }

    [Fact]
    public void S3_WithoutARegion_StopsStartup_NamingTheSetting()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings
            {
                DefaultProvider = "Database",
                S3 = new S3StorageSettings { BucketName = "iniaai-files", Region = "" },
            }));

        Assert.Contains("FileStorage:S3:Region", refused.Message);
    }

    [Fact]
    public void AnS3CompatibleStore_NeedsNoRegion()
    {
        FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings
        {
            DefaultProvider = "S3",
            S3 = new S3StorageSettings { BucketName = "iniaai-files", Region = "", ServiceUrl = "http://localhost:8333" },
        });
    }

    [Fact]
    public void MinIO_WithoutAnEndpoint_StopsStartup_NamingTheSetting()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings { DefaultProvider = "MinIO" }));

        Assert.Contains("FileStorage:MinIO:Endpoint", refused.Message);
    }

    [Fact]
    public void AnOverrideNamingAStoreNotSetUp_StopsStartup()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings
            {
                DefaultProvider = "Database",
                TenantOverrides = new Dictionary<string, string> { ["org-1"] = "S3" },
            }));

        Assert.Contains("FileStorage:S3:BucketName", refused.Message);
    }

    [Fact]
    public void AStoreNameThePlatformDoesNotKnow_StopsStartup()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            FileStorageProviders.ThrowIfMisconfigured(new FileStorageSettings { DefaultProvider = "AzureBlob" }));

        Assert.Contains("AzureBlob", refused.Message);
    }

    [Fact]
    public void AFileRecordedInAStoreTheDeploymentLacks_FailsItsRead_InsteadOfReadingTheDatabase()
    {
        var services = new ServiceCollection();
        _ = services.AddKeyedSingleton<IFileStorageService>(FileStorageProviders.DATABASE, (_, _) => new ThrowingFileStorageService());
        using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new FileStorageResolver(provider, NullLogger<FileStorageResolver>.Instance);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("S3"));

        Assert.Contains("S3", refused.Message);
    }
}
