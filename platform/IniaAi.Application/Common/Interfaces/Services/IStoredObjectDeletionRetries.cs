namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Keeps a stored file whose delete failed, so a later pass deletes it. By then no row points at
/// the file, so without this record only the sweep for unreferenced files would find it.
/// </summary>
public interface IStoredObjectDeletionRetries
{
    /// <summary>
    /// Records the failed delete. It never throws: a record it cannot write is logged, and the sweep
    /// still removes the file.
    /// </summary>
    Task RecordAsync(string storageProvider, string organizationId, string category, string key, Exception failure);
}
