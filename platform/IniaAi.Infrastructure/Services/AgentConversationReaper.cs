using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Ends idle conversations, deletes ended ones whole, and settles turns nobody is watching. The
/// bundle bytes go before their rows, and a conversation goes only once all its bundle rows are
/// gone, so a failed byte delete is retried by the next sweep instead of stranding bytes nothing
/// points at. The runs' evidence rows carry no foreign key to the conversation, so they go by the
/// run id prefix every turn's run shares.
/// </summary>
public sealed class AgentConversationReaper : IAgentConversationReaper
{
    private const int MAX_CONVERSATIONS_PER_SWEEP = 200;
    private const int DEFAULT_RETENTION_DAYS = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileStorageManager _storage;
    private readonly AgentHostIdentity _host;
    private readonly AgentConversationPolicies _policies;
    private readonly AgentConversationSettings _settings;
    private readonly ILogger<AgentConversationReaper> _logger;

    public AgentConversationReaper(
        IServiceScopeFactory scopeFactory,
        IFileStorageManager storage,
        AgentHostIdentity host,
        AgentConversationPolicies policies,
        IOptions<AgentConversationSettings> settings,
        ILogger<AgentConversationReaper> logger)
    {
        _scopeFactory = scopeFactory;
        _storage = storage;
        _host = host;
        _policies = policies;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await EndIdleAsync(cancellationToken);
        await PruneOldTurnsAsync(cancellationToken);
        return await DeleteEndedAsync(cancellationToken);
    }

    public async Task<int> SettleOverdueTurnsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationLedger ledger = scope.ServiceProvider.GetRequiredService<AgentConversationLedger>();
        IReadOnlyDictionary<string, AgentConversationPolicy> policies = _policies.All;

        var dispatched = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Where(static row => row.DispatchedAt != null
                && (row.Status == AgentConversationTurnStatus.Queued || row.Status == AgentConversationTurnStatus.Running))
            .Select(static row => new { row.Id, row.DispatchedAt, row.Conversation!.Purpose })
            .ToListAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        TimeSpan margin = TimeSpan.FromMinutes(_settings.OverdueMarginMinutes);
        int settled = 0;

        foreach (var turn in dispatched)
        {
            if (!policies.TryGetValue(turn.Purpose, out AgentConversationPolicy? policy)
                || turn.DispatchedAt!.Value + policy.TurnDeadline + margin > now)
            {
                continue;
            }

            if (await ledger.AbandonAsync(turn.Id, AgentConversationTurnStatus.Failed, AgentConversationTurnErrors.OVERDUE))
            {
                settled++;
            }
        }

        if (settled > 0)
        {
            _logger.LogWarning("Failed {Count} agent conversation turn(s) still in flight past their deadline", settled);
        }

        return settled;
    }

    public async Task<int> AbandonInFlightTurnsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationLedger ledger = scope.ServiceProvider.GetRequiredService<AgentConversationLedger>();

        string host = _host.InstanceId;
        List<string> mine = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Where(row => row.HostInstanceId == host
                && row.DispatchedAt != null
                && (row.Status == AgentConversationTurnStatus.Queued || row.Status == AgentConversationTurnStatus.Running))
            .Select(static row => row.Id)
            .ToListAsync(cancellationToken);

        int settled = 0;
        foreach (string turnId in mine)
        {
            if (await ledger.AbandonAsync(turnId, AgentConversationTurnStatus.Cancelled, AgentConversationTurnErrors.ABANDONED))
            {
                settled++;
            }
        }

        if (settled > 0)
        {
            _logger.LogWarning(
                "Marked {Count} agent conversation turn(s) Cancelled: host {Host} stopped while they were in flight",
                settled,
                host);
        }

        return settled;
    }

    /// <summary>
    /// One conditional update per purpose: the idle test and the no-turn-in-flight test are in
    /// the statement itself, so a conversation that took a message after the sweep looked is
    /// left live.
    /// </summary>
    private async Task EndIdleAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IReadOnlyDictionary<string, AgentConversationPolicy> policies = _policies.All;

        List<string> purposes = await context.Set<AgentConversation>()
            .Where(static row => row.EndedAt == null)
            .Select(static row => row.Purpose)
            .Distinct()
            .ToListAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        foreach (string purpose in purposes)
        {
            int retentionDays = policies.TryGetValue(purpose, out AgentConversationPolicy? policy)
                ? policy.RetentionDays
                : DEFAULT_RETENTION_DAYS;
            DateTime idleBefore = now.AddDays(-Math.Max(1, retentionDays));

            _ = await context.Set<AgentConversation>()
                .Where(row => row.Purpose == purpose
                    && row.EndedAt == null
                    && row.LastActivityAt < idleBefore
                    && !row.Turns.Any(turn => turn.Status == AgentConversationTurnStatus.Queued
                        || turn.Status == AgentConversationTurnStatus.Running))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EndedAt, now), cancellationToken);
        }
    }

    /// <summary>
    /// A conversation that stays in use keeps its rows, but a turn settled longer ago than the
    /// purpose's retention window loses its runs' evidence rows, its session bundles, the step
    /// labels it showed and whatever its consumer keeps of it: those hold what the agent read,
    /// such as a past report's outputs or names, and must not outlive the window because the
    /// conversation is still active. The answer stays: it is the conversation the user sees.
    /// </summary>
    private async Task PruneOldTurnsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IReadOnlyDictionary<string, AgentConversationPolicy> policies = _policies.All;
        DateTime now = DateTime.UtcNow;
        int shortest = policies.Count == 0 ? DEFAULT_RETENTION_DAYS : policies.Values.Min(static policy => policy.RetentionDays);
        DateTime earliestCutoff = now.AddDays(-Math.Max(1, Math.Min(shortest, DEFAULT_RETENTION_DAYS)));

        var old = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Where(row => row.Conversation!.EndedAt == null
                && row.CompletedAt < earliestCutoff
                && row.Status != AgentConversationTurnStatus.Queued
                && row.Status != AgentConversationTurnStatus.Running)
            .Select(static row => new
            {
                row.Id,
                row.ConversationId,
                row.Number,
                row.CompletedAt,
                row.Conversation!.Purpose,
                row.Conversation.OrganizationId,
            })
            .ToListAsync(cancellationToken);

        var expired = old
            .Where(row => row.CompletedAt < now.AddDays(-Math.Max(1, policies.TryGetValue(row.Purpose, out AgentConversationPolicy? policy)
                ? policy.RetentionDays
                : DEFAULT_RETENTION_DAYS)))
            .GroupBy(static row => (row.ConversationId, row.Purpose, row.OrganizationId))
            .Take(MAX_CONVERSATIONS_PER_SWEEP);

        Dictionary<string, IAgentConversationConsumer> consumers = scope.ServiceProvider.GetServices<IAgentConversationConsumer>()
            .GroupBy(static consumer => consumer.Purpose, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var conversation in expired)
        {
            List<string> turnIds = [.. conversation.Select(static row => row.Id)];
            _ = await context.Set<AgentConversationTurn>()
                .Where(row => turnIds.Contains(row.Id) && row.ActivityJson != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActivityJson, (string?)null), cancellationToken);
            if (consumers.TryGetValue(conversation.Key.Purpose, out IAgentConversationConsumer? consumer))
            {
                await consumer.PruneTurnsAsync(turnIds, cancellationToken);
            }

            int newestExpired = conversation.Max(static row => row.Number);
            _ = await AgentRunSessionBundleStorage.ReleaseConversationAsync(
                context, _storage, _logger, conversation.Key.ConversationId, newestExpired + 1, cancellationToken);

            foreach (int number in conversation.Select(static row => row.Number))
            {
                string firstPass = AgentRunIds.ForConversationTurn(
                    conversation.Key.Purpose, conversation.Key.ConversationId, number, AgentRunIds.FIRST_PASS);
                string laterPasses = firstPass + AgentRunIds.CONVERSATION_PASS_SEPARATOR;
                _ = await context.Set<AgentRunTraceFile>()
                    .Where(row => row.OrganizationId == conversation.Key.OrganizationId
                        && row.BlockId == null
                        && (row.RunExecutionId == firstPass || row.RunExecutionId.StartsWith(laterPasses)))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    private async Task<int> DeleteEndedAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        List<AgentConversation> ended = await context.Set<AgentConversation>()
            .AsNoTracking()
            .Where(static row => row.EndedAt != null
                && !row.Turns.Any(turn => turn.Status == AgentConversationTurnStatus.Queued
                    || turn.Status == AgentConversationTurnStatus.Running))
            .OrderBy(static row => row.EndedAt)
            .Take(MAX_CONVERSATIONS_PER_SWEEP)
            .ToListAsync(cancellationToken);

        int deleted = 0;
        foreach (AgentConversation conversation in ended)
        {
            if (await DeleteAsync(context, conversation, cancellationToken))
            {
                deleted++;
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} ended agent conversation(s)", deleted);
        }

        return deleted;
    }

    private async Task<bool> DeleteAsync(
        UserDataDbContext context,
        AgentConversation conversation,
        CancellationToken cancellationToken)
    {
        if (!await AgentRunSessionBundleStorage.ReleaseConversationAsync(
            context, _storage, _logger, conversation.Id, beforeTurn: null, cancellationToken))
        {
            return false;
        }

        string prefix = AgentRunIds.ConversationRunPrefix(conversation.Purpose, conversation.Id);
        _ = await context.Set<AgentRunTraceFile>()
            .Where(row => row.OrganizationId == conversation.OrganizationId
                && row.BlockId == null
                && row.RunExecutionId.StartsWith(prefix))
            .ExecuteDeleteAsync(cancellationToken);

        int removed = await context.Set<AgentConversation>()
            .Where(row => row.Id == conversation.Id
                && row.EndedAt != null
                && !row.Turns.Any(turn => turn.Status == AgentConversationTurnStatus.Queued
                    || turn.Status == AgentConversationTurnStatus.Running))
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }
}
