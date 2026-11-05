using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Models.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The shared conversation turn service. A singleton: the process-wide lock serializes the
/// numbering, the cap check and every dispatch, and each call opens its own scope for the
/// database. A dispatch claims its turn with a conditional update, so a second host can never
/// start the same turn twice.
/// </summary>
public sealed class AgentConversationTurns : IAgentConversationTurns, IDisposable
{
    private const string JOB_DESCRIPTION = "Agent conversation message";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobsService _jobs;
    private readonly IAgentConversationEvents _events;
    private readonly AgentHostIdentity _host;
    private readonly AgentConversationPolicies _policies;
    private readonly ILogger<AgentConversationTurns> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AgentConversationTurns(
        IServiceScopeFactory scopeFactory,
        IJobsService jobs,
        IAgentConversationEvents events,
        AgentHostIdentity host,
        AgentConversationPolicies policies,
        ILogger<AgentConversationTurns> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _events = events;
        _host = host;
        _policies = policies;
        _logger = logger;
        _lifetime = lifetime;
    }

    private bool Stopping => _lifetime?.ApplicationStopping.IsCancellationRequested == true;

    public async Task<ErrorOr<AgentConversationTurn>> SubmitAsync(
        AgentConversationSubmission submission,
        CancellationToken cancellationToken = default)
    {
        AgentConversationTurn turn;
        AgentConversation conversation;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            AgentConversation? live = await context.Set<AgentConversation>()
                .SingleOrDefaultAsync(
                    row => row.Id == submission.ConversationId
                        && row.OrganizationId == submission.OrganizationId
                        && row.UserId == submission.UserId
                        && row.Purpose == submission.Purpose
                        && row.EndedAt == null,
                    cancellationToken);
            if (live is null)
            {
                return AgentConversationErrors.NotFound;
            }

            conversation = live;
            IQueryable<AgentConversationTurn> turns = context.Set<AgentConversationTurn>();
            bool busy = await turns.AnyAsync(
                row => row.ConversationId == conversation.Id
                    && (row.Status == AgentConversationTurnStatus.Queued || row.Status == AgentConversationTurnStatus.Running),
                cancellationToken);

            if (!busy)
            {
                AgentConversationPolicy policy = _policies.Of(conversation.Purpose);
                int running = await CountRunningAsync(context, submission.UserId, conversation.Purpose, cancellationToken);
                if (running >= policy.MaxRunningTurnsPerUser)
                {
                    return AgentConversationErrors.TooManyRunning(policy.MaxRunningTurnsPerUser);
                }
            }

            int lastNumber = await turns
                .Where(row => row.ConversationId == conversation.Id)
                .Select(static row => (int?)row.Number)
                .MaxAsync(cancellationToken) ?? 0;

            DateTime now = DateTime.UtcNow;
            conversation.ModelId = submission.ModelId;
            conversation.LastActivityAt = now;

            turn = new AgentConversationTurn
            {
                Id = submission.TurnId ?? Guid.NewGuid().ToString(),
                ConversationId = conversation.Id,
                UserId = submission.UserId,
                Number = lastNumber + 1,
                UserMessage = submission.Message,
                ModelId = submission.ModelId,
                ContextRef = submission.ContextRef,
                CreatedAt = now,
            };
            _ = context.Set<AgentConversationTurn>().Add(turn);

            try
            {
                _ = await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return AgentConversationErrors.NotFound;
            }
            catch (DbUpdateException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Recording turn {Number} of conversation {ConversationId} collided with another write",
                    turn.Number,
                    conversation.Id);

                return AgentConversationErrors.Busy;
            }
        }
        finally
        {
            _ = _lock.Release();
        }

        _events.Publish(AgentConversationLedger.Change(conversation, turn));
        _ = await DispatchNextAsync(conversation.Id, CancellationToken.None);

        return await ReadTurnAsync(turn.Id) ?? turn;
    }

    public async Task<ErrorOr<Success>> CancelAsync(AgentConversationTurnKey key, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        AgentConversationTurn? turn = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Include(static row => row.Conversation)
            .SingleOrDefaultAsync(
                row => row.Id == key.TurnId
                    && row.UserId == key.UserId
                    && row.Conversation!.OrganizationId == key.OrganizationId
                    && row.Conversation.Purpose == key.Purpose,
                cancellationToken);

        if (turn is null)
        {
            return AgentConversationErrors.NotFound;
        }

        if (!turn.Status.IsInFlight())
        {
            return Result.Success;
        }

        // The job is this host's for as long as the job service still lists it; cancelling marks
        // it finished at once, so "not finished" would stop recognising it on a second click.
        if (turn.JobId is not null && _jobs.GetJob(turn.JobId) is not null)
        {
            _jobs.CancelJob(turn.JobId);
            return Result.Success;
        }

        AgentConversationLedger ledger = scope.ServiceProvider.GetRequiredService<AgentConversationLedger>();

        if (turn.DispatchedAt is null)
        {
            await CancelWaitingAsync(ledger, turn.Id, AgentConversationTurnErrors.CANCELLED, cancellationToken);
            _ = await DispatchNextAsync(turn.ConversationId, CancellationToken.None);
            return Result.Success;
        }

        if (turn.HostInstanceId == _host.InstanceId)
        {
            _ = await ledger.AbandonAsync(turn.Id, AgentConversationTurnStatus.Cancelled, AgentConversationTurnErrors.CANCELLED);
            _ = await DispatchReadyAsync(turn.UserId, CancellationToken.None);
            return Result.Success;
        }

        IAgentRunExecutor? executor = scope.ServiceProvider.GetService<IAgentRunExecutor>();
        if (turn.RunExecutionId is null || executor is null)
        {
            return AgentConversationErrors.StartingElsewhere;
        }

        // The host that runs the turn sees its run end Cancelled and settles and bills it.
        await executor.CancelQuietlyAsync(turn.RunExecutionId);
        return Result.Success;
    }

    public async Task EndAsync(
        string organizationId,
        string userId,
        string purpose,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            DateTime now = DateTime.UtcNow;
            int ended = await context.Set<AgentConversation>()
                .Where(row => row.Id == conversationId
                    && row.OrganizationId == organizationId
                    && row.UserId == userId
                    && row.Purpose == purpose
                    && row.EndedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EndedAt, now), cancellationToken);
            if (ended == 0)
            {
                return;
            }

            List<string> waiting = await context.Set<AgentConversationTurn>()
                .Where(row => row.ConversationId == conversationId
                    && row.Status == AgentConversationTurnStatus.Queued
                    && row.DispatchedAt == null)
                .Select(static row => row.Id)
                .ToListAsync(cancellationToken);

            AgentConversationLedger ledger = scope.ServiceProvider.GetRequiredService<AgentConversationLedger>();
            foreach (string turnId in waiting)
            {
                _ = await ledger.SettleAsync(
                    turnId,
                    new AgentConversationVerdict(
                        AgentConversationTurnStatus.Cancelled, null, 0m, AgentConversationTurnErrors.CONVERSATION_ENDED));
            }
        }
        finally
        {
            _ = _lock.Release();
        }
    }

    public Task<int> DispatchReadyAsync(CancellationToken cancellationToken = default)
    {
        return DispatchReadyAsync(userId: null, cancellationToken);
    }

    /// <summary>
    /// Starts waiting turns oldest first, one conversation at a time, each only when its
    /// conversation has no dispatched turn in flight and its user is under the cap. Null
    /// <paramref name="userId"/> covers every user.
    /// </summary>
    public async Task<int> DispatchReadyAsync(string? userId, CancellationToken cancellationToken)
    {
        if (Stopping)
        {
            return 0;
        }

        List<string> conversationIds;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            var waiting = await context.Set<AgentConversationTurn>()
                .AsNoTracking()
                .Where(row => row.Status == AgentConversationTurnStatus.Queued
                    && row.DispatchedAt == null
                    && (userId == null || row.UserId == userId))
                .Select(static row => new { row.ConversationId, row.CreatedAt })
                .ToListAsync(cancellationToken);

            conversationIds = [.. waiting
                .GroupBy(static row => row.ConversationId, StringComparer.Ordinal)
                .OrderBy(static group => group.Min(static row => row.CreatedAt))
                .Select(static group => group.Key)];
        }

        int started = 0;
        foreach (string conversationId in conversationIds)
        {
            if (await DispatchNextAsync(conversationId, cancellationToken))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>
    /// Starts the conversation's lowest waiting turn when nothing of the conversation is running
    /// and its user is under the cap. Skipped while the host is stopping, so a turn settled by
    /// shutdown does not start its successor; the next startup does.
    /// </summary>
    public async Task<bool> DispatchNextAsync(string conversationId, CancellationToken cancellationToken)
    {
        if (Stopping)
        {
            return false;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await DispatchNextLockedAsync(conversationId, cancellationToken);
        }
        finally
        {
            _ = _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private async Task<bool> DispatchNextLockedAsync(string conversationId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IQueryable<AgentConversationTurn> turns = context.Set<AgentConversationTurn>().AsNoTracking();

        if (await turns.AnyAsync(
            row => row.ConversationId == conversationId
                && row.DispatchedAt != null
                && (row.Status == AgentConversationTurnStatus.Queued || row.Status == AgentConversationTurnStatus.Running),
            cancellationToken))
        {
            return false;
        }

        AgentConversationTurn? next = await turns
            .Include(static row => row.Conversation)
            .Where(row => row.ConversationId == conversationId
                && row.Status == AgentConversationTurnStatus.Queued
                && row.DispatchedAt == null)
            .OrderBy(static row => row.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null)
        {
            return false;
        }

        AgentConversation conversation = next.Conversation!;
        AgentConversationPolicy policy = _policies.Of(conversation.Purpose);
        if (await CountRunningAsync(context, next.UserId, conversation.Purpose, cancellationToken) >= policy.MaxRunningTurnsPerUser)
        {
            return false;
        }

        string turnId = next.Id;
        var job = new DelegatedJob(
            conversation.OrganizationId,
            next.UserId,
            JOB_DESCRIPTION,
            (provider, token) => provider.GetRequiredService<AgentConversationTurnJob>().RunAsync(turnId, token));

        DateTime now = DateTime.UtcNow;
        int claimed = await context.Set<AgentConversationTurn>()
            .Where(row => row.Id == turnId
                && row.Status == AgentConversationTurnStatus.Queued
                && row.DispatchedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.DispatchedAt, now)
                    .SetProperty(row => row.JobId, job.Id)
                    .SetProperty(row => row.HostInstanceId, _host.InstanceId),
                cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        try
        {
            _jobs.Execute(job, shouldUseQueue: false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Starting conversation turn {TurnId} failed", turnId);
            _ = await scope.ServiceProvider.GetRequiredService<AgentConversationLedger>().SettleAsync(
                turnId,
                new AgentConversationVerdict(AgentConversationTurnStatus.Failed, null, 0m, AgentConversationTurnErrors.START_FAILED));

            return false;
        }

        return true;
    }

    /// <summary>
    /// Under the lock, so a dispatch cannot claim the turn between the check and the write.
    /// </summary>
    private async Task CancelWaitingAsync(
        AgentConversationLedger ledger,
        string turnId,
        string error,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            AgentConversationTurn? turn = await ReadTurnAsync(turnId);
            if (turn is not null && turn.IsWaiting())
            {
                _ = await ledger.SettleAsync(
                    turnId, new AgentConversationVerdict(AgentConversationTurnStatus.Cancelled, null, 0m, error));
            }
        }
        finally
        {
            _ = _lock.Release();
        }
    }

    private async Task<AgentConversationTurn?> ReadTurnAsync(string turnId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == turnId, CancellationToken.None);
    }

    private static Task<int> CountRunningAsync(
        UserDataDbContext context,
        string userId,
        string purpose,
        CancellationToken cancellationToken)
    {
        return context.Set<AgentConversationTurn>().CountAsync(
            row => row.UserId == userId
                && row.DispatchedAt != null
                && (row.Status == AgentConversationTurnStatus.Queued || row.Status == AgentConversationTurnStatus.Running)
                && row.Conversation!.Purpose == purpose,
            cancellationToken);
    }
}
