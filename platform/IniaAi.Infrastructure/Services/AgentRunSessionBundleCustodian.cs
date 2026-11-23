using ErrorOr;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Takes custody of a finished run's session bundle: fetch, re-hash against the digest the
/// runtime declared, store the bytes, then commit the pointer row — in that order, so a row
/// never names bytes the platform does not hold. Writes through a short-lived
/// <see cref="UserDataDbContext"/> of its own, the way
/// <see cref="AgentRunTracePersister"/> does, because the executor calls from teardown paths
/// whose request scope may already be dying.
/// <para>
/// Every failure path answers false and logs. Custody is the one part of the settle sequence
/// that must not withhold an acknowledgement: the bundle only saves a redo leg the cost of
/// starting over, while a withheld acknowledgement holds runtime retention capacity for the
/// full window.
/// </para>
/// </summary>
public class AgentRunSessionBundleCustodian : IAgentRunSessionBundleCustodian
{
    private readonly IAgentRuntimeClient _client;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IFileStorageManager _storage;
    private readonly ILogger<AgentRunSessionBundleCustodian> _logger;

    public AgentRunSessionBundleCustodian(
        IAgentRuntimeClient client,
        IServiceScopeFactory serviceScopeFactory,
        IFileStorageManager storage,
        ILogger<AgentRunSessionBundleCustodian> logger)
    {
        _client = client;
        _serviceScopeFactory = serviceScopeFactory;
        _storage = storage;
        _logger = logger;
    }

    public async Task<bool> TakeCustodyAsync(
        AgentRunSessionBundleAnchor anchor,
        string executionId,
        int attempt,
        int droppedOutFileCount = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ErrorOr<AgentRunCheckpoint> fetched = await _client.GetRunCheckpointAsync(executionId, cancellationToken);
            if (fetched.IsError)
            {
                LogFetchOutcome(executionId, fetched.Errors);

                return false;
            }

            AgentRunCheckpoint checkpoint = fetched.Value;
            if (checkpoint.Content.LongLength > AgentRunSessionBundleContent.MAX_BUNDLE_BYTES)
            {
                _logger.LogError(
                    "The session bundle of run {ExecutionId} is {SizeBytes} bytes, past the "
                        + "{MaxBytes}-byte cap the runtime writes under; it is refused unread",
                    executionId,
                    checkpoint.Content.LongLength,
                    AgentRunSessionBundleContent.MAX_BUNDLE_BYTES);

                return false;
            }

            string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(checkpoint.Content));
            if (!computedSha256.Equals(checkpoint.DeclaredSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "The session bundle of run {ExecutionId} failed sha256 verification against the digest the "
                        + "runtime declared; it is refused and no pointer row is recorded",
                    executionId);

                return false;
            }

            return await StoreAsync(
                anchor, executionId, attempt, droppedOutFileCount, checkpoint, computedSha256, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Taking custody of the session bundle of run {ExecutionId} failed; the run keeps its acknowledgement "
                    + "and a later leg starts from scratch",
                executionId);

            return false;
        }
    }

    public async Task<AgentRunSessionBundle?> ResolveNewestBeforeAsync(
        string organizationId,
        string blockId,
        int dispatchRound,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await context.Set<AgentRunSessionBundle>()
            .AsNoTracking()
            .Where(row => row.OrganizationId == organizationId
                && row.BlockId == blockId
                && row.Round < dispatchRound)
            .OrderByDescending(static row => row.Round)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// A bundle goes to the organization's store: one row in the UserData <c>Files</c> table under
    /// the Database store, an object in the bucket under S3 or MinIO. The read takes it whole,
    /// which is affordable only because bundles are short-lived (every non-Failed terminal deletes
    /// the execution's lot, the retention sweep takes the rest) and capped at 64 MiB by the runtime.
    /// </summary>
    private async Task<bool> StoreAsync(
        AgentRunSessionBundleAnchor anchor,
        string executionId,
        int attempt,
        int droppedOutFileCount,
        AgentRunCheckpoint checkpoint,
        string sha256,
        CancellationToken cancellationToken)
    {
        StorageProvider provider = _storage.GetProviderForTenant(anchor.OrganizationId);
        string storageKey = AgentRunSessionBundleStorage.NewStorageKey();

        // Read before the store: nothing here is compensated yet, so an exception the stamp
        // reader's filter does not cover costs nothing at this point and would strand the bytes
        // at any point after it.
        string? versionStamp = AgentRunSessionBundleContent.TryReadVersionStamp(checkpoint.Content);

        _ = await _storage.StoreFileAsync(
            provider.ToString(),
            anchor.OrganizationId,
            StorageCategories.AgentSessionBundles,
            storageKey,
            checkpoint.Content,
            AgentRunSessionBundleStorage.CONTENT_TYPE);

        var stored = new AgentRunSessionBundle
        {
            OrganizationId = anchor.OrganizationId,
            BlockId = anchor.BlockId,
            ConversationId = anchor.ConversationId,
            Round = anchor.Round,
            RunExecutionId = executionId,
            Attempt = attempt,
            Segment = AgentRunCheckpoint.TERMINAL_SEGMENT,
            Sha256 = sha256,
            SizeBytes = checkpoint.Content.LongLength,
            StorageProvider = provider.ToString(),
            StorageKey = storageKey,
            VersionStamp = versionStamp,
            DroppedOutFileCount = droppedOutFileCount,
        };

        StoredBytes? superseded;
        try
        {
            superseded = await CommitAsync(stored, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Recording the session bundle pointer row for run {ExecutionId} failed; removing the bytes it would "
                    + "have named so nothing is stored without an index into it",
                executionId);

            _ = await AgentRunSessionBundleStorage.DeleteBytesQuietlyAsync(_storage, StoredBytes.Of(stored), _logger);

            return false;
        }

        if (superseded is { } supersededBytes)
        {
            _ = await AgentRunSessionBundleStorage.DeleteBytesQuietlyAsync(_storage, supersededBytes, _logger);
        }

        _logger.LogInformation(
            "Session bundle of run {ExecutionId} is in platform custody: owner {Owner} round {Round}, "
                + "{SizeBytes} bytes at {StorageProvider}/{StorageKey}, stamp {VersionStamp}",
            executionId,
            anchor.BlockId ?? anchor.ConversationId,
            anchor.Round,
            stored.SizeBytes,
            stored.StorageProvider,
            stored.StorageKey,
            stored.VersionStamp ?? "(unstamped)");

        return true;
    }

    public async Task<AgentRunSessionBundle?> ResolveNewestForConversationAsync(
        string organizationId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await context.Set<AgentRunSessionBundle>()
            .AsNoTracking()
            .Where(row => row.OrganizationId == organizationId && row.ConversationId == conversationId)
            .OrderByDescending(static row => row.Round)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<byte[]?> ReadBytesAsync(
        AgentRunSessionBundle held,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bytes;
        try
        {
            bytes = await _storage.RetrieveFileBytesAsync(
                held.StorageProvider,
                held.OrganizationId,
                StorageCategories.AgentSessionBundles,
                held.StorageKey);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The session bundle of run {RunExecutionId} could not be read back from "
                    + "{StorageProvider}/{StorageKey}; the resume path starts from scratch",
                held.RunExecutionId,
                held.StorageProvider,
                held.StorageKey);

            return null;
        }

        string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!computedSha256.Equals(held.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "The session bundle of run {RunExecutionId} no longer matches the digest custody recorded; "
                    + "the resume path starts from scratch",
                held.RunExecutionId);

            return null;
        }

        return bytes;
    }

    /// <summary>
    /// Upserts on the natural key (owner, round) rather than the row id, the same rule the
    /// block writer applies to attempt rows: a re-run of the same leg mints the same run id, so
    /// keying on the row would leave the round pointing at a bundle the re-run replaced.
    /// Returns the row's prior storage location when one was superseded, so its bytes can go
    /// once the new row is committed.
    /// <para>
    /// Unlike <see cref="AgentRunTracePersister"/>, a lost insert race is not retried: only one
    /// leg of a (block, round) runs at a time, so a race here means something is wrong, and
    /// custody fails open — the loser drops its own bytes and the winner's row stands.
    /// </para>
    /// </summary>
    private async Task<StoredBytes?> CommitAsync(
        AgentRunSessionBundle stored,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        // Projected, not tracked: the new row is what gets written, so materializing the old one
        // into the change tracker would put two instances of the same key in it.
        IQueryable<AgentRunSessionBundle> sameOwner = stored.BlockId is string blockId
            ? context.Set<AgentRunSessionBundle>().Where(row => row.BlockId == blockId)
            : context.Set<AgentRunSessionBundle>().Where(row => row.ConversationId == stored.ConversationId);

        var existing = await sameOwner
            .AsNoTracking()
            .Where(row => row.Round == stored.Round)
            .Select(row => new
            {
                row.Id,
                row.OrganizationId,
                row.RunExecutionId,
                row.StorageProvider,
                row.StorageKey,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            _ = context.Entry(stored).State = EntityState.Added;
            _ = await context.SaveChangesAsync(cancellationToken);

            return null;
        }

        // Every column rides across because the whole row is rewritten under the old row's id —
        // a field-by-field copy would silently keep the superseded value of any column added later.
        stored.Id = existing.Id;
        _ = context.Entry(stored).State = EntityState.Modified;
        _ = await context.SaveChangesAsync(cancellationToken);

        return new StoredBytes(
            existing.OrganizationId,
            existing.RunExecutionId,
            existing.StorageProvider,
            existing.StorageKey);
    }

    /// <summary>
    /// A run that published no bundle is the ordinary case for a leg that failed before its
    /// first model turn, so it is recorded as information; anything else is a fault worth an
    /// error line, because a member that should have been resumable will now redo from scratch.
    /// </summary>
    private void LogFetchOutcome(string executionId, List<Error> errors)
    {
        if (errors.Exists(static error => error.Code == AgentRunLifecycleErrors.NOT_FOUND))
        {
            _logger.LogInformation(
                "Run {ExecutionId} published no session bundle; nothing is taken into custody",
                executionId);

            return;
        }

        _logger.LogError(
            "Fetching the session bundle of run {ExecutionId} failed ({Errors}); the run keeps its acknowledgement "
                + "and a later leg starts from scratch",
            executionId,
            string.Join("; ", errors.Select(static error => $"{error.Code}: {error.Description}")));
    }
}
