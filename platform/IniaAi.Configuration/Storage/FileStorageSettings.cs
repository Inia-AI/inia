namespace IniaAi.Configuration.Storage;

/// <summary>
/// Well-known file storage category names. Only a MinIO store without a fixed bucket uses them for a
/// location (bucket name = {orgId}-{category}); every other store finds a file by its key alone.
/// </summary>
public static class StorageCategories
{
    /// <summary>
    /// User-uploaded and processed documents (PDFs, Word, Excel, images).
    /// </summary>
    public const string Documents = "documents";

    /// <summary>
    /// Json2Xlsx Excel templates.
    /// </summary>
    public const string Json2XlsxTemplates = "json2xlsx-templates";

    /// <summary>
    /// Session bundles of finished agent runs, held so a later leg can continue the session
    /// (<c>AgentRunSessionBundle</c>). Which provider they land on, and why that is affordable,
    /// is documented where the store happens: <c>AgentRunSessionBundleCustodian</c>.
    /// </summary>
    public const string AgentSessionBundles = "agent-session-bundles";
}

/// <summary>
/// Configuration settings for file storage providers
/// </summary>
public class FileStorageSettings
{
    public const string SECTION_NAME = "FileStorage";

    /// <summary>
    /// The default storage provider to use (Database, MinIO, S3)
    /// </summary>
    public string DefaultProvider { get; set; } = "Database";

    /// <summary>
    /// MinIO storage configuration
    /// </summary>
    public MinioStorageSettings? MinIO { get; set; }

    /// <summary>
    /// AWS S3 storage configuration
    /// </summary>
    public S3StorageSettings? S3 { get; set; }

    /// <summary>
    /// Storage bucket category mappings
    /// </summary>
    public Dictionary<string, string>? StorageBuckets { get; set; }

    /// <summary>
    /// Per-tenant storage provider overrides
    /// Key: OrganizationId, Value: StorageProvider name
    /// </summary>
    public Dictionary<string, string>? TenantOverrides { get; set; }
}

/// <summary>
/// MinIO storage provider settings
/// </summary>
public class MinioStorageSettings
{
    /// <summary>
    /// MinIO server endpoint (e.g., "localhost:9000")
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Access key for MinIO authentication
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret key for MinIO authentication
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Use SSL for MinIO connections
    /// </summary>
    public bool UseSSL { get; set; } = false;

    /// <summary>
    /// MinIO region (optional)
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Fixed bucket name for on-premise / single-tenant deployments.
    /// When set, all files are stored in this single bucket instead of
    /// the default <c>{orgId}-{category}</c> per-tenant bucket pattern.
    /// The bucket must be pre-created by the customer's infrastructure team.
    /// <para>
    /// <b>Important:</b> This setting disables per-organization bucket isolation.
    /// It is intended only for on-premise deployments where the customer controls
    /// infrastructure and multi-tenancy is not required. Do not use in shared
    /// (SaaS / multi-tenant) environments.
    /// </para>
    /// </summary>
    public string? BucketName { get; set; }
}

/// <summary>
/// AWS S3 storage provider settings. The provider exists only when <see cref="BucketName"/> is set.
/// </summary>
public class S3StorageSettings
{
    /// <summary>
    /// The bucket every file is stored in, under its organization's prefix. The stack creates it;
    /// the platform never creates a bucket.
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// AWS region for S3 buckets
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// AWS access key ID (optional if using IAM roles)
    /// </summary>
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// AWS secret access key (optional if using IAM roles)
    /// </summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// S3 service URL, for an S3-compatible store instead of Amazon S3. With it the client uses
    /// path-style addresses and sends checksums only where an operation requires one.
    /// </summary>
    public string? ServiceUrl { get; set; }
}