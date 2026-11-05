namespace IniaAi.Configuration.Storage;

/// <summary>
/// Well-known file storage category names.
/// Categories determine the bucket/container suffix: bucket name = {orgId}-{category}.
/// When <see cref="MinioStorageSettings.BucketName"/> is set (on-premise mode), all
/// categories share a single fixed bucket and this suffix convention is bypassed.
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
/// AWS S3 storage provider settings
/// </summary>
public class S3StorageSettings
{
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
    /// S3 service URL (for S3-compatible services)
    /// </summary>
    public string? ServiceUrl { get; set; }
}