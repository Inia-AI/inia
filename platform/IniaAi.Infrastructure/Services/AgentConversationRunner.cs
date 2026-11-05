using System.Globalization;
using System.Text;
using System.Text.Json;
using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Common.Resources;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The shared layer's conversation turn: one continuation run per message. A turn resumes the
/// previous turn's session when the request allows it, custody holds that turn's bundle and it
/// ran on the same model; otherwise it starts fresh with the transcript of the earlier turns in
/// its prompt, which is also the fallback the executor runs when a continuation fails to restore
/// at no spend. Every write to the turn row goes through the <see cref="AgentConversationLedger"/>
/// in short-lived contexts of its own, because a turn outlives any request scope.
/// </summary>
public sealed class AgentConversationRunner : IAgentConversationRunner
{
    private const string TRANSCRIPT_RESOURCE = "agent-conversation-transcript.md";
    private const string EXCHANGE_RESOURCE = "agent-conversation-exchange.md";
    private const string EXCHANGES_SLOT = "{exchanges}";
    private const string MESSAGE_SLOT = "{message}";
    private const string USER_SLOT = "{user}";
    private const string ANSWER_SLOT = "{answer}";
    private const string NO_ANSWER_TEXT = "(no answer)";
    private const string BUDGET_ERROR = "The message reached its spending limit before the agent finished.";
    private const string DEADLINE_ERROR = "The message ran out of time before the agent finished.";
    private const string GENERIC_FAILURE_ERROR = "The agent could not finish this message ({0}).";
    private const string NO_ANSWER_ERROR = "The agent finished without an answer.";
    private const string MACHINERY_ERROR = "The agent could not be reached ({0}).";
    private const string AGENT_UNAVAILABLE_ERROR = "The agent is not available on this deployment.";
    private const decimal MIN_ROUND_BUDGET_USD = 0.05m;
    private const long MIN_ROUND_DEADLINE_MS = 60_000;
    private const string ROUND_SEPARATOR = "\n\n---\n\n";

    private static readonly JsonSerializerOptions _activityJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<string> _transcriptTemplate = new(() => ReadResource(TRANSCRIPT_RESOURCE));
    private static readonly Lazy<string> _exchangeTemplate = new(() => ReadResource(EXCHANGE_RESOURCE));

    private readonly IAgentRunExecutor _executor;
    private readonly IAgentRunSessionBundleCustodian _custodian;
    private readonly IAgentRunSessionBundleReaper _bundleReaper;
    private readonly AgentConversationLedger _ledger;
    private readonly IAgentConversationEvents _events;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentRuntimeSettings _settings;
    private readonly ILogger<AgentConversationRunner> _logger;

    public AgentConversationRunner(
        IAgentRunExecutor executor,
        IAgentRunSessionBundleCustodian custodian,
        IAgentRunSessionBundleReaper bundleReaper,
        AgentConversationLedger ledger,
        IAgentConversationEvents events,
        IServiceScopeFactory scopeFactory,
        IOptions<AgentRuntimeSettings> settings,
        ILogger<AgentConversationRunner> logger)
    {
        _executor = executor;
        _custodian = custodian;
        _bundleReaper = bundleReaper;
        _ledger = ledger;
        _events = events;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AgentConversationTurnOutcome> RunTurnAsync(
        AgentConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        var attempts = new List<AgentRunAttempt>();
        AgentConversationTurn? turn = null;
        AgentConversation? conversation = null;

        try
        {
            (turn, conversation, List<Exchange> earlier) = await LoadAsync(request.TurnId, cancellationToken);

            if (!turn.Status.IsInFlight())
            {
                return new AgentConversationTurnOutcome(
                    turn.Status, turn.AnswerMarkdown, turn.CostUsd, turn.Error, turn.ResumedSession);
            }

            return await RunAsync(request, turn, conversation, earlier, attempts, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = await SettleFaultAsync(
                request, turn, conversation, attempts, AgentConversationTurnStatus.Cancelled, AgentConversationTurnErrors.CANCELLED);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Conversation turn {TurnId} failed outside its run", request.TurnId);
            decimal spent = await SettleFaultAsync(
                request, turn, conversation, attempts, AgentConversationTurnStatus.Failed, AgentConversationTurnErrors.INTERNAL_FAILURE);

            return new AgentConversationTurnOutcome(
                AgentConversationTurnStatus.Failed, null, spent, AgentConversationTurnErrors.INTERNAL_FAILURE, ResumedSession: false);
        }
    }

    private async Task<AgentConversationTurnOutcome> RunAsync(
        AgentConversationTurnRequest request,
        AgentConversationTurn turn,
        AgentConversation conversation,
        List<Exchange> earlier,
        List<AgentRunAttempt> attempts,
        CancellationToken cancellationToken)
    {
        AgentRunContinuation? continuation = request.ResumeSession
            ? await PlanContinuationAsync(conversation, turn, cancellationToken)
            : null;
        string freshPrompt = WithTranscript(earlier, request.Prompt);
        string prompt = continuation is null ? freshPrompt : request.Prompt;
        bool continuedFirstRound = continuation is not null;
        DateTime? startedAt = null;
        IReadOnlyList<AgentRunDocument> extraDocuments = [];

        var bought = new List<AgentRunAttempt>();
        var traceFiles = new List<AgentRunTraceFile>();
        bool collected = false;
        RoundOutcome? settledRound = null;
        bool resumed = false;

        for (int round = 1; ; round++)
        {
            RunBudget budget = Remaining(request, bought, startedAt);
            startedAt ??= DateTime.UtcNow;
            RoundOutcome outcome = await RunRoundAsync(
                request,
                turn,
                conversation,
                new RoundPlan(prompt, continuation, continuation is null ? null : freshPrompt, bought.Count, request.ResumeSession, extraDocuments),
                budget,
                attempts,
                cancellationToken);

            bought.AddRange(outcome.Attempts);
            if (outcome.TraceFiles is not null)
            {
                traceFiles.AddRange(outcome.TraceFiles);
                collected = true;
            }

            if (round == 1)
            {
                resumed = continuedFirstRound && outcome.Attempts.Count > 0 && !outcome.Attempts.Any(static a => a.RanContinuationFallback);
            }

            if (outcome.Status != AgentConversationTurnStatus.Completed)
            {
                if (outcome.Status == AgentConversationTurnStatus.Failed && settledRound is { Status: AgentConversationTurnStatus.Completed })
                {
                    _logger.LogWarning(
                        "Conversation {ConversationId} turn {Turn} round {Round} failed ({Error}); the turn keeps the previous round's answer",
                        conversation.Id, turn.Number, round, outcome.Error);
                }
                else
                {
                    settledRound = outcome;
                }

                break;
            }

            settledRound = outcome;
            if (request.ReviewRound is null)
            {
                break;
            }

            RunBudget left = Remaining(request, bought, startedAt);
            bool lastRound = round >= request.MaxRounds || left.Exhausted;
            AgentConversationRoundReview review = await request.ReviewRound(
                new AgentConversationRound(round, lastRound, outcome.Answer!, OutFilesOf(outcome.TraceFiles)),
                cancellationToken);
            if (review.ContinueWith is null || lastRound)
            {
                break;
            }

            freshPrompt = WithTranscript(earlier, request.Prompt + ROUND_SEPARATOR + review.ContinueWith);
            extraDocuments = review.Documents;
            continuation = request.ResumeSession
                ? await PlanRoundContinuationAsync(conversation, turn, bought[^1].ExecutionId, cancellationToken)
                : null;
            prompt = continuation is null ? freshPrompt : review.ContinueWith;
            _logger.LogInformation(
                "Conversation {ConversationId} turn {Turn} goes on to round {Round} at the consumer's request ({Mode})",
                conversation.Id, turn.Number, round + 1, continuation is null ? "fresh" : "continued");
        }

        decimal cost = bought.TotalCostUsd();
        (AgentConversationTurnStatus status, string? answer, string? error) = (settledRound!.Status, settledRound.Answer, settledRound.Error);
        IReadOnlyList<AgentRunTraceFile>? allTraceFiles = collected ? traceFiles : null;

        AgentConversationSettlement settled = await _ledger.SettleAsync(
            turn.Id,
            new AgentConversationVerdict(status, answer, cost, error)
            {
                ActivityJson = ActivityJsonOf(allTraceFiles, request.LiveActivity?.Projection),
                RunCount = bought.Count,
                Resumed = resumed,
            });
        await _ledger.RecordSpendAsync(
            conversation,
            turn.Number,
            turn.ModelId,
            request.Billing,
            settled.UnbilledUsd,
            bought.Count,
            settled.Written ? allTraceFiles : null,
            wholeTurn: settled.UnbilledUsd >= cost,
            attempts: bought);

        if (status == AgentConversationTurnStatus.Completed && request.ResumeSession)
        {
            await _bundleReaper.ReleaseConversationBeforeAsync(conversation.Id, turn.Number);
        }

        AgentConversationTurn final = settled.Turn ?? turn;

        return final.Status == status
            ? new AgentConversationTurnOutcome(status, answer, cost, error, resumed)
            : new AgentConversationTurnOutcome(final.Status, final.AnswerMarkdown, final.CostUsd, final.Error, final.ResumedSession);
    }

    /// <summary>One round's run: the executor's whole lifecycle, read into the turn's vocabulary.</summary>
    private async Task<RoundOutcome> RunRoundAsync(
        AgentConversationTurnRequest request,
        AgentConversationTurn turn,
        AgentConversation conversation,
        RoundPlan plan,
        RunBudget budget,
        List<AgentRunAttempt> attempts,
        CancellationToken cancellationToken)
    {
        int passOffset = plan.RunsBought;
        AgentRunStartRequest startRequest = new(
            AgentRunIds.ForConversationTurn(conversation.Purpose, conversation.Id, turn.Number, passOffset + AgentRunIds.FIRST_PASS),
            plan.Prompt,
            RunToken: null)
        {
            Documents = [.. request.Documents, .. plan.ExtraDocuments],
            SkillFiles = request.SkillFiles,
            Model = turn.ModelId,
            Effort = conversation.Effort,
            SystemPrompt = request.SystemPrompt,
            DeadlineMs = budget.DeadlineMs,
            MaxBudgetUsd = budget.MaxBudgetUsd,
            MaxTurns = request.MaxTurns,
            Continuation = plan.Continuation,
            AdmissionClass = _settings.SendAdmissionClass ? AgentRunAdmissionClass.Chat : null,
        };

        var options = new AgentRunExecutionOptions(
            PollDeadline(budget.DeadlineMs),
            AgentRunActivityRegistration.ForConversation(
                startRequest.ExecutionId, conversation.OrganizationId, conversation.Purpose, conversation.Id, turn.Number))
        {
            AdmissionPatience = request.AdmissionPatience,
            MaxPasses = _settings.MaxRunPasses,
            PollErrorTolerance = _settings.PollErrorTolerance,
            ExecutionIdForPass = pass => AgentRunIds.ForConversationTurn(
                conversation.Purpose, conversation.Id, turn.Number, passOffset + pass),
            TraceAnchor = new AgentRunTraceAnchor(conversation.OrganizationId, BlockId: null),
            SessionBundleAnchor = plan.KeepBundle
                ? AgentRunSessionBundleAnchor.ForConversation(conversation.OrganizationId, conversation.Id, turn.Number)
                : null,
            ContinuationFallbackPrompt = plan.FallbackPrompt,
            OnAttempt = attempts.Add,
            LiveActivity = request.LiveActivity,
            OnRunStarted = (started, token) => MarkRunningAsync(
                turn.Id,
                started with { Pass = passOffset + started.Pass },
                passOffset == 0 ? plan.Continuation is not null && !started.RanContinuationFallback : null,
                token),
        };

        ErrorOr<AgentRunConclusion> concluded = await _executor.ExecuteAsync(startRequest, options, cancellationToken);

        IReadOnlyList<AgentRunAttempt> ledger = concluded.IsError
            ? AgentRunAttempts.Of(concluded.FirstError)
            : concluded.Value.Attempts;
        IReadOnlyList<AgentRunTraceFile>? traceFiles = concluded.IsError
            ? AgentRunAttempts.TraceFilesOf(concluded.FirstError)
            : concluded.Value.TraceFiles;

        (AgentConversationTurnStatus status, string? answer, string? error) = concluded.IsError
            ? (AgentConversationTurnStatus.Failed, null, MachineryErrorOf(concluded.FirstError))
            : Read(concluded.Value.Monitor);

        return new RoundOutcome(status, answer, error, ledger, traceFiles);
    }

    private static string MachineryErrorOf(Error error)
    {
        return error.Code == AgentRunAdmissionErrors.CHAT_DISABLED
            ? AGENT_UNAVAILABLE_ERROR
            : string.Format(CultureInfo.InvariantCulture, MACHINERY_ERROR, error.Code);
    }

    /// <summary>What is left of the turn's budget and deadline once the runs so far are paid for.</summary>
    /// <remarks>The clock starts with the first round, so the first round gets the whole declared deadline.</remarks>
    private static RunBudget Remaining(AgentConversationTurnRequest request, List<AgentRunAttempt> bought, DateTime? startedAt)
    {
        decimal? budget = request.MaxBudgetUsd is decimal cap ? cap - bought.TotalCostUsd() : null;
        long elapsedMs = startedAt is DateTime started ? (long)(DateTime.UtcNow - started).TotalMilliseconds : 0;
        long? deadline = request.DeadlineMs is long declared ? declared - elapsedMs : null;

        return new RunBudget(budget, deadline);
    }

    /// <summary>
    /// The next round resumes the session the round before it left: the bundle custody holds for
    /// this turn, and only when it is that run's. Anything else starts the round fresh.
    /// </summary>
    private async Task<AgentRunContinuation?> PlanRoundContinuationAsync(
        AgentConversation conversation,
        AgentConversationTurn turn,
        string lastExecutionId,
        CancellationToken cancellationToken)
    {
        AgentRunSessionBundle? held = await _custodian.ResolveNewestForConversationAsync(
            conversation.OrganizationId, conversation.Id, cancellationToken);

        if (held is null || held.Round != turn.Number || held.RunExecutionId != lastExecutionId)
        {
            _logger.LogInformation(
                "Conversation {ConversationId} turn {Turn}: the last round left no session bundle to continue",
                conversation.Id, turn.Number);

            return null;
        }

        return await ContinuationFromAsync(conversation, turn, held, cancellationToken);
    }

    private static IReadOnlyList<AgentRunRecarriedFile> OutFilesOf(IReadOnlyList<AgentRunTraceFile>? traceFiles)
    {
        if (traceFiles is null)
        {
            return [];
        }

        int lastAttempt = traceFiles.Count == 0 ? 0 : traceFiles.Max(static file => file.Attempt);
        string? lastRun = traceFiles.LastOrDefault(file => file.Attempt == lastAttempt)?.RunExecutionId;

        return AgentRunContinuationBuilder.OutFilesOf(traceFiles, lastRun, lastAttempt);
    }

    private sealed record RoundPlan(
        string Prompt,
        AgentRunContinuation? Continuation,
        string? FallbackPrompt,
        int RunsBought,
        bool KeepBundle,
        IReadOnlyList<AgentRunDocument> ExtraDocuments);

    private sealed record Exchange(string UserMessage, string? AnswerMarkdown);

    private sealed record RunBudget(decimal? MaxBudgetUsd, long? DeadlineMs)
    {
        public bool Exhausted => MaxBudgetUsd < MIN_ROUND_BUDGET_USD || DeadlineMs < MIN_ROUND_DEADLINE_MS;
    }

    private sealed record RoundOutcome(
        AgentConversationTurnStatus Status,
        string? Answer,
        string? Error,
        IReadOnlyList<AgentRunAttempt> Attempts,
        IReadOnlyList<AgentRunTraceFile>? TraceFiles);

    /// <summary>
    /// The settle of a turn the runner could not finish normally: the spend its attempts reported
    /// so far is the turn's cost. Never throws, because it runs on the way out of a fault.
    /// </summary>
    private async Task<decimal> SettleFaultAsync(
        AgentConversationTurnRequest request,
        AgentConversationTurn? turn,
        AgentConversation? conversation,
        List<AgentRunAttempt> attempts,
        AgentConversationTurnStatus status,
        string error)
    {
        decimal spent = attempts.TotalCostUsd();

        try
        {
            AgentConversationSettlement settled = await _ledger.SettleAsync(
                request.TurnId,
                new AgentConversationVerdict(status, null, spent, error) { RunCount = attempts.Count });

            AgentConversation? owner = conversation ?? settled.Conversation;
            AgentConversationTurn? row = turn ?? settled.Turn;
            if (owner is not null && row is not null)
            {
                await _ledger.RecordSpendAsync(
                    owner, row.Number, row.ModelId, request.Billing, settled.UnbilledUsd, attempts.Count, traceFiles: null);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Settling conversation turn {TurnId} as {Status} failed; the overdue sweep settles it later",
                request.TurnId,
                status);
        }

        return spent;
    }

    private async Task<(AgentConversationTurn Turn, AgentConversation Conversation, List<Exchange> Earlier)> LoadAsync(
        string turnId,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        AgentConversationTurn turn = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Include(static row => row.Conversation)
            .SingleOrDefaultAsync(row => row.Id == turnId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation turn '{turnId}' does not exist");

        List<Exchange> earlier = await context.Set<AgentConversationTurn>()
            .AsNoTracking()
            .Where(row => row.ConversationId == turn.ConversationId
                && row.Number < turn.Number
                && row.Status == AgentConversationTurnStatus.Completed)
            .OrderBy(static row => row.Number)
            .Select(static row => new Exchange(row.UserMessage, row.AnswerMarkdown))
            .ToListAsync(cancellationToken);

        return (turn, turn.Conversation!, earlier);
    }

    /// <summary>
    /// A conversation resumes only from the turn just before this one: an older bundle would
    /// wake the agent without the latest exchange, and the transcript is the honest fallback.
    /// Every other reason the bundle cannot be used falls back the same way.
    /// </summary>
    private async Task<AgentRunContinuation?> PlanContinuationAsync(
        AgentConversation conversation,
        AgentConversationTurn turn,
        CancellationToken cancellationToken)
    {
        if (turn.Number <= 1)
        {
            return null;
        }

        AgentRunSessionBundle? held = await _custodian.ResolveNewestForConversationAsync(
            conversation.OrganizationId, conversation.Id, cancellationToken);

        string? reason = held switch
        {
            null => "no earlier turn left a session bundle",
            _ when held.Round != turn.Number - 1 => $"the newest bundle is from turn {held.Round}, not the previous turn",
            _ => AgentRunContinuationBuilder.DescribeBundleProblem(held, turn.ModelId),
        };

        if (reason is not null)
        {
            _logger.LogInformation(
                "Conversation {ConversationId} turn {Turn} starts fresh with the transcript: {Reason}",
                conversation.Id, turn.Number, reason);

            return null;
        }

        return await ContinuationFromAsync(conversation, turn, held!, cancellationToken);
    }

    /// <summary>
    /// A continuation from a held bundle, its run's out/ files re-carried; null when those files
    /// cannot be re-carried or the bundle cannot be read back.
    /// </summary>
    private async Task<AgentRunContinuation?> ContinuationFromAsync(
        AgentConversation conversation,
        AgentConversationTurn turn,
        AgentRunSessionBundle held,
        CancellationToken cancellationToken)
    {
        List<AgentRunRecarriedFile> outFiles = await OutFilesOfAsync(held, cancellationToken);
        (AgentRunContinuation? continuation, string? problem) =
            await AgentRunContinuationBuilder.BuildAsync(held, outFiles, _custodian, cancellationToken);
        if (continuation is null)
        {
            _logger.LogInformation(
                "Conversation {ConversationId} turn {Turn} starts fresh: {Problem}",
                conversation.Id, turn.Number, problem);
        }

        return continuation;
    }

    private async Task<List<AgentRunRecarriedFile>> OutFilesOfAsync(
        AgentRunSessionBundle held,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        List<AgentRunTraceFile> rows = await context.Set<AgentRunTraceFile>()
            .AsNoTracking()
            .Where(row => row.RunExecutionId == held.RunExecutionId
                && row.Attempt == held.Attempt
                && row.Path.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX))
            .ToListAsync(cancellationToken);

        return AgentRunContinuationBuilder.OutFilesOf(rows, held.RunExecutionId, held.Attempt);
    }

    /// <summary>
    /// Moves the turn to Running only while it is still in flight: a turn another party settled
    /// meanwhile stays settled. A later round's run leaves the resumed flag as its first round set it.
    /// </summary>
    private async Task MarkRunningAsync(
        string turnId,
        AgentRunStarted started,
        bool? resumed,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        AgentConversationTurn turn = await context.Set<AgentConversationTurn>()
            .Include(static row => row.Conversation)
            .SingleAsync(row => row.Id == turnId, cancellationToken);
        if (!turn.Status.IsInFlight())
        {
            return;
        }

        bool announce = turn.Status != AgentConversationTurnStatus.Running;
        turn.Status = AgentConversationTurnStatus.Running;
        turn.RunExecutionId = started.ExecutionId;
        turn.RunCount = started.Pass;
        if (resumed is bool continued)
        {
            turn.ResumedSession = continued;
        }

        turn.StartedAt ??= DateTime.UtcNow;

        try
        {
            _ = await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return;
        }

        if (announce)
        {
            _events.Publish(AgentConversationLedger.Change(turn.Conversation!, turn));
        }
    }

    private TimeSpan PollDeadline(long? deadlineMs)
    {
        return deadlineMs is long declared
            ? TimeSpan.FromMilliseconds(declared) + TimeSpan.FromSeconds(_settings.RunDeadlineMarginSeconds)
            : TimeSpan.FromMinutes(_settings.MaxPollDeadlineMinutes);
    }

    private static (AgentConversationTurnStatus Status, string? Answer, string? Error) Read(AgentRunMonitor monitor)
    {
        if (monitor.TryGetResult(out AgentRunResult? result))
        {
            return string.IsNullOrWhiteSpace(result.Output)
                ? (AgentConversationTurnStatus.Failed, null, NO_ANSWER_ERROR)
                : (AgentConversationTurnStatus.Completed, result.Output, null);
        }

        if (monitor.Status == AgentRunStatus.Cancelled)
        {
            return (AgentConversationTurnStatus.Cancelled, null, AgentConversationTurnErrors.CANCELLED);
        }

        string subtype = monitor.TryGetError(out AgentRunError? error) && error.Subtype is string reported
            ? reported
            : monitor.Status.ToString();
        string message = subtype switch
        {
            AgentRunSubtypes.BUDGET_EXHAUSTED => BUDGET_ERROR,
            AgentRunSubtypes.DEADLINE_EXCEEDED => DEADLINE_ERROR,
            _ => string.Format(CultureInfo.InvariantCulture, GENERIC_FAILURE_ERROR, subtype),
        };

        return (AgentConversationTurnStatus.Failed, null, message);
    }

    private static string WithTranscript(IReadOnlyList<Exchange> earlier, string prompt)
    {
        if (earlier.Count == 0)
        {
            return prompt;
        }

        var exchanges = new StringBuilder();
        foreach (Exchange exchange in earlier)
        {
            _ = exchanges.Append(_exchangeTemplate.Value
                .Replace(USER_SLOT, exchange.UserMessage, StringComparison.Ordinal)
                .Replace(ANSWER_SLOT, exchange.AnswerMarkdown ?? NO_ANSWER_TEXT, StringComparison.Ordinal));
        }

        return _transcriptTemplate.Value
            .Replace(EXCHANGES_SLOT, exchanges.ToString(), StringComparison.Ordinal)
            .Replace(MESSAGE_SLOT, prompt, StringComparison.Ordinal)
            .TrimEnd();
    }

    private static string? ActivityJsonOf(
        IReadOnlyList<AgentRunTraceFile>? traceFiles,
        AgentActivityProjectionOptions? projection)
    {
        AgentRunTraceFile? pack = traceFiles?.LastOrDefault(
            static file => file.Path == AgentRunTraceTokens.TRACE_FILE_PATH);

        return pack is null
            ? null
            : JsonSerializer.Serialize(
                AgentActivityProjector.Project(pack.Content, projection ?? AgentActivityProjectionOptions.Default),
                _activityJsonOptions);
    }

    private static string ReadResource(string name)
    {
        using var reader = new StreamReader(typeof(AgentConversationRunner).Assembly.ReadResource(name));
        return reader.ReadToEnd();
    }
}
