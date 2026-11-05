using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Services.TokenUsage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The writes every path that settles a conversation turn shares: the runner, the turn's job,
/// cancel, startup recovery and the overdue sweep. A turn is settled only while it is still in
/// flight; a turn another party settled first keeps its verdict and only takes cost it was
/// missing, and only the cost the row did not already carry is billed, so two settlers of one
/// turn never bill it twice. Each write runs in a short-lived context of its own.
/// </summary>
public sealed class AgentConversationLedger
{
    private const string AGENT_RUNTIME_PROVIDER = "agent-runtime";
    private const int MAX_SETTLE_ATTEMPTS = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentConversationEvents _events;
    private readonly ILogger<AgentConversationLedger> _logger;
    private readonly IAgentRunExecutor? _executor;

    public AgentConversationLedger(
        IServiceScopeFactory scopeFactory,
        IAgentConversationEvents events,
        ILogger<AgentConversationLedger> logger,
        IAgentRunExecutor? executor = null)
    {
        _scopeFactory = scopeFactory;
        _events = events;
        _logger = logger;
        _executor = executor;
    }

    /// <summary>
    /// Writes the verdict while the turn is in flight, or only a higher cost onto a turn already
    /// settled. Retries on a concurrent write, which re-reads the row and applies the same rule.
    /// </summary>
    public async Task<AgentConversationSettlement> SettleAsync(string turnId, AgentConversationVerdict verdict)
    {
        for (int attempt = 1; ; attempt++)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            AgentConversationTurn? turn = await context.Set<AgentConversationTurn>()
                .Include(static row => row.Conversation)
                .SingleOrDefaultAsync(row => row.Id == turnId, CancellationToken.None);
            if (turn is null)
            {
                return AgentConversationSettlement.Untouched;
            }

            decimal billedBefore = turn.CostUsd;
            bool settles = turn.Status.IsInFlight();
            DateTime now = DateTime.UtcNow;

            if (settles)
            {
                turn.Status = verdict.Status;
                turn.AnswerMarkdown = verdict.Answer;
                turn.Error = verdict.Error is { Length: > AgentConversationTurn.ERROR_MAX_LENGTH }
                    ? verdict.Error[..AgentConversationTurn.ERROR_MAX_LENGTH]
                    : verdict.Error;
                turn.CompletedAt = now;
                turn.ResumedSession = verdict.Resumed ?? turn.ResumedSession;
                turn.Conversation!.LastActivityAt = now;
            }
            else if (verdict.CostUsd <= turn.CostUsd)
            {
                return new AgentConversationSettlement(false, 0m, turn, turn.Conversation);
            }

            turn.CostUsd = Math.Max(turn.CostUsd, verdict.CostUsd);
            turn.ActivityJson = verdict.ActivityJson ?? turn.ActivityJson;
            turn.RunCount = Math.Max(turn.RunCount, verdict.RunCount);

            try
            {
                _ = await context.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MAX_SETTLE_ATTEMPTS)
            {
                continue;
            }

            // A turn that settled earlier, such as one stopped at its deadline, can learn its full
            // cost later from the run's teardown; anyone showing the cost reads it again.
            if (settles || turn.CostUsd > billedBefore)
            {
                _events.Publish(Change(turn.Conversation!, turn));
            }

            return new AgentConversationSettlement(true, Math.Max(0m, turn.CostUsd - billedBefore), turn, turn.Conversation);
        }
    }

    /// <summary>
    /// Settles a turn whose run nobody is watching any more: cancels the run on the runtime, by
    /// its run id or by the first-pass id a dispatched turn would have started under, settles the
    /// turn with what the teardown says it cost, and bills that. False when the turn had already
    /// settled.
    /// </summary>
    public async Task<bool> AbandonAsync(string turnId, AgentConversationTurnStatus status, string error)
    {
        (AgentConversationTurn Turn, AgentConversation Conversation)? loaded = await LoadAsync(turnId);
        if (loaded is not { } found || !found.Turn.Status.IsInFlight())
        {
            return false;
        }

        string? runId = found.Turn.RunExecutionId ?? (found.Turn.DispatchedAt is null
            ? null
            : AgentRunIds.ForConversationTurn(
                found.Conversation.Purpose, found.Conversation.Id, found.Turn.Number, AgentRunIds.FIRST_PASS));

        decimal cost = 0m;
        if (runId is not null && _executor is not null)
        {
            AgentRunMonitor monitor = await _executor.AbandonQuietlyAsync(
                runId, new AgentRunTraceAnchor(found.Conversation.OrganizationId, BlockId: null));
            cost = monitor.EstimatedCostUsd;
        }

        AgentConversationSettlement settled = await SettleAsync(
            turnId,
            new AgentConversationVerdict(status, null, cost, error)
            {
                RunCount = runId is null ? 0 : Math.Max(1, found.Turn.RunCount),
            });

        if (settled.UnbilledUsd > 0m)
        {
            await RecordSpendAsync(
                found.Conversation,
                found.Turn.Number,
                found.Turn.ModelId,
                BillingOf(found.Conversation),
                settled.UnbilledUsd,
                runs: 1,
                traceFiles: null);
        }

        return settled.Written;
    }

    /// <summary>
    /// Billed like a workflow run (<see cref="AgentRunBilling"/>): the turn's tokens priced from
    /// the pricing table, one record per model, and a spend record carrying only what no pricing
    /// row covered. <paramref name="spent"/> is the runtime's figure for the part of the turn this
    /// call bills. When <paramref name="wholeTurn"/> is false an earlier settle already billed part
    /// of the turn, so the tokens, which cover the whole turn, are recorded at zero cost and the
    /// runtime's figure for the rest is the bill. <paramref name="attempts"/>, when given, are the
    /// runs the turn bought: one whose trace pack was not collected is billed at the runtime's
    /// figure. Never throws.
    /// </summary>
    public async Task RecordSpendAsync(
        AgentConversation conversation,
        int turnNumber,
        string modelId,
        AgentConversationBilling? billing,
        decimal spent,
        int runs,
        IReadOnlyList<AgentRunTraceFile>? traceFiles,
        bool wholeTurn = true,
        IReadOnlyList<AgentRunAttempt>? attempts = null)
    {
        if (spent <= 0m && traceFiles is null)
        {
            return;
        }

        if (billing is null)
        {
            _logger.LogError(
                "Conversation {ConversationId} turn {Turn} spent {Spent} USD, but no consumer is registered for purpose "
                    + "{Purpose} to bill it under",
                conversation.Id,
                turnNumber,
                spent,
                conversation.Purpose);

            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITokenUsageService usage = scope.ServiceProvider.GetRequiredService<ITokenUsageService>();

            var bill = new AgentRunBill(
                UsageRecord(conversation, billing, modelId, billing.Description),
                Math.Max(1, runs),
                spent,
                AgentRunTraceTokens.Read(traceFiles),
                modelId,
                turnNumber,
                PriceTokens: wholeTurn,
                UncoveredRuntimeCostUsd: attempts is null ? 0m : AgentRunTraceTokens.UncoveredCostUsd(attempts, traceFiles));

            _ = await AgentRunBilling.RecordAsync(bill, usage, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Recording the spend of conversation {ConversationId} turn {Turn} failed",
                conversation.Id,
                turnNumber);
        }
    }

    /// <summary>The billing the purpose's consumer declares, or null when none is registered.</summary>
    public AgentConversationBilling? BillingOf(AgentConversation conversation)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IAgentConversationConsumer? consumer = scope.ServiceProvider
            .GetServices<IAgentConversationConsumer>()
            .FirstOrDefault(candidate => candidate.Purpose == conversation.Purpose);

        return consumer?.Billing(conversation);
    }

    public static AgentConversationTurnChanged Change(AgentConversation conversation, AgentConversationTurn turn)
    {
        return new AgentConversationTurnChanged(
            conversation.OrganizationId, conversation.Purpose, conversation.Id, turn.Id, turn.Number, turn.Status);
    }

    private async Task<(AgentConversationTurn Turn, AgentConversation Conversation)?> LoadAsync(string turnId)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        AgentConversationTurn? turn = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Include(static row => row.Conversation)
            .SingleOrDefaultAsync(row => row.Id == turnId, CancellationToken.None);

        return turn is null ? null : (turn, turn.Conversation!);
    }

    private static LlmUsageRecord UsageRecord(
        AgentConversation conversation,
        AgentConversationBilling billing,
        string modelId,
        string description)
    {
        return new LlmUsageRecord
        {
            OrganizationId = conversation.OrganizationId,
            UserId = conversation.UserId,
            WorkflowTypeId = billing.WorkflowTypeId,
            UsageType = billing.UsageType,
            ExecutionContext = billing.ExecutionContext,
            Provider = AGENT_RUNTIME_PROVIDER,
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = modelId,
            Components = [],
            Description = description,
        };
    }

}

/// <summary>How a turn ended, as a settler writes it.</summary>
public sealed record AgentConversationVerdict(
    AgentConversationTurnStatus Status,
    string? Answer,
    decimal CostUsd,
    string? Error)
{
    public string? ActivityJson { get; init; }

    public int RunCount { get; init; }

    public bool? Resumed { get; init; }
}

/// <param name="Written">Whether this settler wrote the row.</param>
/// <param name="UnbilledUsd">The cost this write added to the row, which the settler bills.</param>
public sealed record AgentConversationSettlement(
    bool Written,
    decimal UnbilledUsd,
    AgentConversationTurn? Turn,
    AgentConversation? Conversation)
{
    public static AgentConversationSettlement Untouched { get; } = new(false, 0m, null, null);
}
