using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The byte half of bundle custody, shared by the custodian that writes rows and by every
/// remover — the reaper and the soft-delete cleanup: all address storage the same way, and all
/// treat a byte-level fault as something to log rather than to fail on.
/// </summary>
internal static class AgentRunSessionBundleStorage
{
    internal const string CONTENT_TYPE = "application/octet-stream";

    /// <summary>
    /// Storage keys are minted fresh per store rather than derived from the run, so a supersede
    /// can write the new bytes before it removes the old ones. A derived key would collide with
    /// the row it is replacing — an editor re-run mints the same run id — and force a
    /// delete-then-store order in which a failed delete loses the new bundle too.
    /// </summary>
    internal static string NewStorageKey()
    {
        return $"agent-session-bundle-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Deletes the bytes of a bundle whose pointer row is gone. Null when the bytes are gone, the
    /// fault when they are not: the delete is then recorded for a retry, and the fault is logged, so
    /// a caller with nothing to do about it ignores the answer.
    /// </summary>
    internal static async Task<Exception?> DeleteBytesQuietlyAsync(
        IFileStorageManager storage,
        StoredBytes bundle,
        ILogger logger)
    {
        Exception? failure = await storage.ReleaseFileAsync(
            bundle.StorageProvider,
            bundle.OrganizationId,
            StorageCategories.AgentSessionBundles,
            bundle.StorageKey);
        if (failure is not null)
        {
            logger.LogWarning(
                failure,
                "Deleting the session bundle bytes of run {RunExecutionId} at {StorageProvider}/{StorageKey} failed; "
                    + "the pointer row is gone, and the delete is retried later",
                bundle.RunExecutionId,
                bundle.StorageProvider,
                bundle.StorageKey);
        }

        return failure;
    }

    /// <summary>
    /// Releases a conversation's bundles, those of turns before <paramref name="beforeTurn"/> when
    /// it is given. Bytes go first, and a row goes only with its bytes, so a failed delete keeps its
    /// row for the next release or the conversation's retention to retry. True when every bundle's
    /// bytes are gone.
    /// </summary>
    internal static async Task<bool> ReleaseConversationAsync(
        UserDataDbContext context,
        IFileStorageManager storage,
        ILogger logger,
        string conversationId,
        int? beforeTurn,
        CancellationToken cancellationToken)
    {
        List<AgentRunSessionBundle> bundles = await context.Set<AgentRunSessionBundle>()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversationId && (beforeTurn == null || row.Round < beforeTurn))
            .ToListAsync(cancellationToken);

        bool allBytesGone = true;
        foreach (AgentRunSessionBundle bundle in bundles)
        {
            try
            {
                await storage.DeleteFileAsync(bundle.StorageProvider, bundle.OrganizationId, StorageCategories.AgentSessionBundles, bundle.StorageKey);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Deleting the session bundle bytes of run {RunExecutionId} at {StorageProvider}/{StorageKey} failed; "
                        + "the pointer row stays for the next release to retry",
                    bundle.RunExecutionId,
                    bundle.StorageProvider,
                    bundle.StorageKey);
                allBytesGone = false;
                continue;
            }

            _ = await context.Set<AgentRunSessionBundle>()
                .Where(row => row.Id == bundle.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return allBytesGone;
    }
}

/// <summary>
/// Where one bundle's bytes are and which run they belong to — everything a delete needs and
/// nothing it does not. Deliberately not an <see cref="AgentRunSessionBundle"/>: a superseded
/// row's location outlives the row, and half-filling an entity to carry four strings gives the
/// next reader a record whose other fields are lies.
/// </summary>
internal sealed record StoredBytes(
    string OrganizationId,
    string RunExecutionId,
    string StorageProvider,
    string StorageKey)
{
    internal static StoredBytes Of(AgentRunSessionBundle bundle)
    {
        return new StoredBytes(
            bundle.OrganizationId,
            bundle.RunExecutionId,
            bundle.StorageProvider,
            bundle.StorageKey);
    }
}
