using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Removes bundle rows and the bytes they name, in that order: a stranded blob costs storage,
/// while a row naming bytes that are already gone would answer a resume with a bundle nobody
/// can read. Works in a fresh scope of its own because the terminal caller invokes it from
/// dying request scopes and teardown paths.
/// </summary>
public class AgentRunSessionBundleReaper : IAgentRunSessionBundleReaper
{
    private const int MAX_ROWS_PER_SWEEP = 500;

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IFileStorageManager _storage;
    private readonly ILogger<AgentRunSessionBundleReaper> _logger;

    public AgentRunSessionBundleReaper(
        IServiceScopeFactory serviceScopeFactory,
        IFileStorageManager storage,
        ILogger<AgentRunSessionBundleReaper> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _storage = storage;
        _logger = logger;
    }

    public async Task ReleaseExecutionAsync(
        string workflowExecutionId,
        string organizationId,
        AgentRunSessionBundleRelease release)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            // The execution's blocks first, then its bundles by block id. A correlated EXISTS
            // over the bundle table would have to scan it — the only predicate available there
            // is the organization, which no index covers and which barely narrows a deployment.
            List<string> blockIds = await context.Blocks
                .AsNoTracking()
                .Where(block => BlockTypesExtensions.AgentFamily.Contains(block.Type)
                    && block.Section!.WorkflowId == workflowExecutionId)
                .Select(block => block.Id)
                .ToListAsync();

            if (blockIds.Count == 0)
            {
                return;
            }

            List<AgentRunSessionBundle> held = await context.Set<AgentRunSessionBundle>()
                .Where(row => row.OrganizationId == organizationId
                    && row.BlockId != null
                    && blockIds.Contains(row.BlockId))
                .ToListAsync();

            List<AgentRunSessionBundle> condemned = release == AgentRunSessionBundleRelease.Everything
                ? held
                : [.. held
                    .GroupBy(static row => row.BlockId!, StringComparer.Ordinal)
                    .SelectMany(static block => block
                        .OrderByDescending(static row => row.Round)
                        .Skip(1))];

            if (condemned.Count == 0)
            {
                return;
            }

            await DeleteAsync(context, condemned);

            _logger.LogInformation(
                "Released {Count} session bundle(s) of execution {ExecutionId} at its terminal ({Release})",
                condemned.Count,
                workflowExecutionId,
                release);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Releasing the session bundles of execution {ExecutionId} failed; the retention sweep collects "
                    + "whatever is left",
                workflowExecutionId);
        }
    }

    public async Task ReleaseConversationBeforeAsync(string conversationId, int keepFromTurn)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            _ = await AgentRunSessionBundleStorage.ReleaseConversationAsync(
                context, _storage, _logger, conversationId, keepFromTurn, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Releasing the earlier session bundles of conversation {ConversationId} failed; the conversation's "
                    + "own retention collects them",
                conversationId);
        }
    }

    public async Task<int> SweepExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        // Bounded per sweep: the normal result is nothing at all, but the sweep exists for the
        // abnormal case, and loading an unbounded backlog into one list is how a backstop becomes
        // the outage. The interval drains what is left.
        List<AgentRunSessionBundle> expired = await context.Set<AgentRunSessionBundle>()
            .Where(row => row.CreatedAt < createdBefore)
            .OrderBy(static row => row.CreatedAt)
            .Take(MAX_ROWS_PER_SWEEP)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        await DeleteAsync(context, expired, cancellationToken);

        return expired.Count;
    }

    /// <summary>
    /// One commit removes every row, then the bytes go one at a time — a byte-level fault is
    /// logged and the rest still go, because a sweep that stops at the first stranded blob
    /// leaves everything behind it stranded too.
    /// </summary>
    private async Task DeleteAsync(
        UserDataDbContext context,
        List<AgentRunSessionBundle> bundles,
        CancellationToken cancellationToken = default)
    {
        context.Set<AgentRunSessionBundle>().RemoveRange(bundles);
        _ = await context.SaveChangesAsync(cancellationToken);

        foreach (AgentRunSessionBundle bundle in bundles)
        {
            _ = await AgentRunSessionBundleStorage.DeleteBytesQuietlyAsync(
                _storage, StoredBytes.Of(bundle), _logger);
        }
    }
}
