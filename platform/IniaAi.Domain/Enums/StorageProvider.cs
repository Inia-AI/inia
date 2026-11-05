namespace IniaAi.Domain.Enums;

/// <summary>
/// Defines the available storage providers for file storage
/// </summary>
public enum StorageProvider
{
    /// <summary>
    /// Files stored in database FileStorage table
    /// </summary>
    NotDefined = 0,
    /// <summary>
    /// Files stored in database FileStorage table
    /// </summary>
    Database = 1,

    /// <summary>
    /// Files stored in Amazon S3
    /// </summary>
    S3 = 2,

    /// <summary>
    /// Files stored in MinIO
    /// </summary>
    MinIO = 3
}
