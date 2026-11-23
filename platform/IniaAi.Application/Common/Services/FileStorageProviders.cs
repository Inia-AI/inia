using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;

namespace IniaAi.Application.Common.Services;

/// <summary>
/// The file stores a deployment can set up: the one map between a store's name, in settings and in
/// a row, and its <see cref="StorageProvider"/>, and the startup check that every store the settings
/// name is set up. Names match in any letter case.
/// </summary>
public static class FileStorageProviders
{
    public const string DATABASE = "Database";
    public const string S3 = "S3";
    public const string MINIO = "Minio";

    /// <summary>
    /// The store a name stands for. An empty name, and a row's <see cref="StorageProvider.NotDefined"/>,
    /// are the database. Null for a name the platform does not know.
    /// </summary>
    public static StorageProvider? Parse(string? name)
    {
        if (string.IsNullOrEmpty(name) || Named(name, nameof(StorageProvider.NotDefined)) || Named(name, DATABASE))
        {
            return StorageProvider.Database;
        }

        if (Named(name, S3))
        {
            return StorageProvider.S3;
        }

        return Named(name, MINIO) ? StorageProvider.MinIO : null;
    }

    /// <summary>
    /// The store a name stands for, as <see cref="Parse"/> reads it. A name the platform does not know
    /// throws: startup refuses one in the settings, so it can only be a fault.
    /// </summary>
    public static StorageProvider ParseKnown(string? name)
    {
        return Parse(name)
            ?? throw new InvalidOperationException($"No file store is named '{name}'. The stores are {DATABASE}, {S3} and {MINIO}.");
    }

    /// <summary>The name a store is registered and looked up under.</summary>
    public static string NameOf(StorageProvider provider)
    {
        return provider switch
        {
            StorageProvider.S3 => S3,
            StorageProvider.MinIO => MINIO,
            _ => DATABASE,
        };
    }

    /// <summary>The registered name of the store a name stands for, or null for a name the platform does not know.</summary>
    public static string? Normalize(string? name)
    {
        return Parse(name) is { } provider ? NameOf(provider) : null;
    }

    /// <summary>Whether the S3 store is set up: it needs the stack's bucket.</summary>
    public static bool HasS3(FileStorageSettings settings)
    {
        return !string.IsNullOrEmpty(settings.S3?.BucketName);
    }

    /// <summary>Whether the MinIO store is set up: it needs an endpoint.</summary>
    public static bool HasMinio(FileStorageSettings settings)
    {
        return !string.IsNullOrEmpty(settings.MinIO?.Endpoint);
    }

    /// <summary>
    /// Stops startup when the default store or an organization's override names a store the
    /// deployment does not set up, so no file is recorded in one store while written to another,
    /// and when the S3 store has no region to send its requests to.
    /// </summary>
    public static void ThrowIfMisconfigured(FileStorageSettings settings)
    {
        if (settings.S3 is { BucketName.Length: > 0 } s3 && string.IsNullOrEmpty(s3.Region) && string.IsNullOrEmpty(s3.ServiceUrl))
        {
            throw new InvalidOperationException(
                "File storage sets up the S3 store, but FileStorage:S3:Region is not set. Set it to the region of the bucket in FileStorage:S3:BucketName.");
        }

        IEnumerable<string> named = (settings.TenantOverrides?.Values ?? Enumerable.Empty<string>()).Prepend(settings.DefaultProvider);
        foreach (string name in named)
        {
            StorageProvider? store = Parse(name);
            if (store is null)
            {
                throw new InvalidOperationException(
                    $"File storage names the store '{name}', which the platform does not know. Set FileStorage:DefaultProvider and every FileStorage:TenantOverrides value to Database, S3 or Minio.");
            }

            if (store == StorageProvider.S3 && !HasS3(settings))
            {
                throw new InvalidOperationException(
                    "File storage names the S3 store, but FileStorage:S3:BucketName is not set. Set it to the stack's files bucket, or name another store.");
            }

            if (store == StorageProvider.MinIO && !HasMinio(settings))
            {
                throw new InvalidOperationException(
                    "File storage names the MinIO store, but FileStorage:MinIO:Endpoint is not set. Set the MinIO endpoint, or name another store.");
            }
        }
    }

    private static bool Named(string name, string store)
    {
        return name.Equals(store, StringComparison.OrdinalIgnoreCase);
    }
}
