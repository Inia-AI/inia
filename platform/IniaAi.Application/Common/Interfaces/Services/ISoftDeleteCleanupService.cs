namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Result of a soft-delete cleanup operation.
/// </summary>
public record CleanupResult(
    int TotalDeleted,
    Dictionary<string, int> DeletedPerTable,
    int FilesDeleted,
    List<string> Errors);

/// <summary>
/// Service that hard-deletes soft-deleted records older than a retention window.
/// Preserves TokenUsages by nullifying their foreign keys before deleting parent records.
/// </summary>
public interface ISoftDeleteCleanupService
{
    /// <summary>
    /// Runs cleanup for a specific organization.
    /// </summary>
    /// <param name="organizationId">The organization to clean up.</param>
    /// <param name="retentionDays">Only delete records soft-deleted more than this many days ago.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="sharedRetentionDays">
    /// The retention for rows no single organization owns (counterparty types, blocks with no section);
    /// <paramref name="retentionDays"/> when null.
    /// </param>
    /// <returns>Summary of what was deleted.</returns>
    Task<CleanupResult> CleanupAsync(string organizationId, int retentionDays, CancellationToken ct = default, int? sharedRetentionDays = null);

    /// <summary>
    /// Removes the named executions of one organization now, soft-deleted or not, with everything
    /// beneath each — the executed workflow clone it ran with its blocks, responses, logs and
    /// report tree, and the output documents its job produced that no job outside the tree also
    /// records, with their pages, embeddings, metadata and stored bytes — through the same
    /// ordered delete and post-commit blob release the retention cleanup runs. Token usage rows
    /// stay for cost accounting. A failed transaction throws, so a caller never records a trim
    /// that did not happen; a stored blob that could not be released is reported in the result's
    /// errors, as the cleanup reports it.
    /// </summary>
    Task<CleanupResult> PurgeExecutionTreesAsync(string organizationId, IReadOnlyCollection<string> executionIds, CancellationToken ct = default);
}
