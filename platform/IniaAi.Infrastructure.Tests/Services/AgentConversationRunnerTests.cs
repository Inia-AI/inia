using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Conversation turns through the real shared layer — executor, trace collector and persister,
/// bundle custodian and reaper, ledger, conversation reaper, token usage service — against a
/// SQLite UserData database, so cascades, conditional updates and the CHECK constraint are the
/// schema's own. Only the runtime's HTTP client and, for one test, a storage that refuses deletes
/// stand in for the outside world.
/// </summary>
public sealed class AgentConversationRunnerTests : IDisposable
{
    private const string STEP_LABELS = "{\"steps\":[{\"label\":\"Read the Acme Holdings report\"}]}";
    private const string ORG_ID = "org-conversation";
    private const string USER_ID = "user-conversation";
    private const string PURPOSE = "edchat";
    private const string MODEL = "claude-opus-5";
    private const string OTHER_MODEL = "claude-haiku-5";
    private const decimal COST_PER_RUN = 0.04m;
    private const decimal TEARDOWN_COST = 0.07m;
    private const string TRACE_PATH = "trace/trace.ndjson";
    private const string OUT_PATH = "out/notes.md";
    private const string OTHER_HOST = "another-host/IniaAi.WebApp";
    private const string GATE_MESSAGE = "The checks failed. Fix these errors.";
    private const string STAMP =
        """{"bundleFormat":"1","sidecar":{"version":"0.3.1005","revision":"x"},"sdk":"2.0.44","model":"claude-opus-5"}""";

    private readonly SqliteConnection _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly StubAgentRuntimeClient _client = new();
    private readonly DeleteRefusingFileStorage _storage = new();
    private readonly Dictionary<string, byte[]> _bundles = [];
    private readonly AgentRuntimeSettings _runtimeSettings = new() { MaxRunPasses = 1, PollErrorTolerance = 0 };

    public AgentConversationRunnerTests()
    {
        DbContextOptions<UserDataDbContext> userDataOptions = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection).Options;
        DbContextOptions<CoreDbContext> coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"conversation-{Guid.NewGuid()}-core").Options;

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddMemoryCache();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(userDataOptions));
        _ = services.AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>());
        _ = services.AddScoped(_ => new CoreDbContext(coreOptions));
        _ = services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        _ = services.AddScoped<ITokenUsageService, TokenUsageService>();
        _ = services.AddSingleton<IFileStorageManager>(new FileStorageManager(
            new TestFileStorageResolver(_storage),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
        _ = services.AddSingleton<IAgentRuntimeClient>(_client);
        _ = services.AddSingleton(Options.Create(_runtimeSettings));
        _ = services.AddSingleton(Options.Create(new AgentConversationSettings { OverdueMarginMinutes = 10 }));
        _ = services.AddSingleton<IDbExceptionAnalyzer, DbExceptionAnalyzer>();
        _ = services.AddSingleton<IAgentRunActivityFeed, AgentRunActivityFeed>();
        _ = services.AddSingleton<AgentRunPollGate>();
        _ = services.AddTransient<IAgentRunTraceCollector, AgentRunTraceCollector>();
        _ = services.AddTransient<IAgentRunTracePersister, AgentRunTracePersister>();
        _ = services.AddTransient<IAgentRunSessionBundleCustodian, AgentRunSessionBundleCustodian>();
        _ = services.AddTransient<IAgentRunSessionBundleReaper, AgentRunSessionBundleReaper>();
        _ = services.AddTransient<IAgentRunExecutor>(provider => new AgentRunExecutor(
            provider.GetRequiredService<IAgentRuntimeClient>(),
            provider.GetRequiredService<AgentRunPollGate>(),
            NullLogger<AgentRunExecutor>.Instance,
            provider.GetRequiredService<IAgentRunTraceCollector>(),
            provider.GetRequiredService<IAgentRunTracePersister>(),
            timeProvider: null,
            activityFeed: provider.GetRequiredService<IAgentRunActivityFeed>(),
            narrationTidier: null,
            bundleCustodian: provider.GetRequiredService<IAgentRunSessionBundleCustodian>()));
        _ = services.AddAgentConversations();
        _ = services.AddTransient<IAgentConversationRunner, AgentConversationRunner>();
        _ = services.AddTransient<IAgentConversationConsumer>(static _ => new ConversationTestConsumer());
        _provider = services.BuildServiceProvider();

        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            _ = context.Organizations.Add(new Organization("Conversation organization", ORG_ID));
            _ = context.Users.Add(new User { Id = USER_ID, OrganizationId = ORG_ID, UserName = USER_ID, FirstName = "Agent", LastName = "User" });
            _ = context.SaveChanges();
        }

        RuntimeSucceedsEveryRun();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private string ThisHost => _provider.GetRequiredService<AgentHostIdentity>().InstanceId;

    [Fact]
    public async Task RunTurnAsync_FirstTurn_CompletesBillsAndTakesTheBundleIntoCustody()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        AgentConversationTurnOutcome outcome = await RunAsync(turn);

        string runId = $"{PURPOSE}-{conversation.Id}-t1";
        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.Equal($"answer of {runId}", outcome.AnswerMarkdown);
        Assert.Equal(COST_PER_RUN, outcome.CostUsd);
        Assert.False(outcome.ResumedSession);

        AgentRunStartRequest started = Assert.Single(_client.StartRequests);
        Assert.Equal(runId, started.ExecutionId);
        Assert.Null(started.Continuation);
        Assert.Equal(MODEL, started.Model);
        Assert.Contains(runId, _client.CompletedExecutionIds);

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        AgentConversationTurn settled = await context.AgentConversationTurns.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AgentConversationTurnStatus.Completed, settled.Status);
        Assert.Equal(runId, settled.RunExecutionId);
        Assert.Equal(COST_PER_RUN, settled.CostUsd);
        Assert.NotNull(settled.ActivityJson);

        AgentRunSessionBundle bundle = await context.AgentRunSessionBundles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(conversation.Id, bundle.ConversationId);
        Assert.Null(bundle.BlockId);
        Assert.Equal(1, bundle.Round);

        Assert.All(
            await context.AgentRunTraceFiles.ToListAsync(TestContext.Current.CancellationToken),
            row => Assert.Null(row.BlockId));

        TokenUsage usage = await SpendRecordAsync(context);
        Assert.Equal(UsageType.LlmEditorAgentChat, usage.UsageType);
        Assert.Equal(TokenExecutionContext.WorkflowEditorAgent, usage.ExecutionContext);
        Assert.Equal(ORG_ID, usage.OrganizationId);
        Assert.Equal(USER_ID, usage.UserId);
        Assert.Equal(COST_PER_RUN, usage.Components.Single().Cost);
    }

    [Fact]
    public async Task RunTurnAsync_SendsNoAdmissionClass_UntilTheSettingIsOn()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);

        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));
        _runtimeSettings.SendAdmissionClass = true;
        _ = await RunAsync(await AddTurnAsync(conversation, 2, MODEL));

        Assert.Null(_client.StartRequests[0].AdmissionClass);
        Assert.Equal(AgentRunAdmissionClass.Chat, _client.StartRequests[1].AdmissionClass);
    }

    [Fact]
    public async Task RunTurnAsync_ContinuationRound_StillAsksForTheChatClass()
    {
        _runtimeSettings.SendAdmissionClass = true;
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        _ = await RunAsync(turn, resume: true, (round, _) => Task.FromResult(round.Round == 1
            ? AgentConversationRoundReview.Continue(GATE_MESSAGE)
            : AgentConversationRoundReview.Finish));

        Assert.Equal(2, _client.StartRequests.Count);
        Assert.NotNull(_client.StartRequests[1].Continuation);
        Assert.All(_client.StartRequests, request => Assert.Equal(AgentRunAdmissionClass.Chat, request.AdmissionClass));
    }

    [Fact]
    public async Task RunTurnAsync_RetryPass_StillAsksForTheChatClass()
    {
        _runtimeSettings.SendAdmissionClass = true;
        _runtimeSettings.MaxRunPasses = 2;
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        string firstRun = $"{PURPOSE}-{conversation.Id}-t1";
        Func<string, ErrorOr<AgentRunMonitor>> succeed = _client.OnGetRun!;
        _client.OnGetRun = id => id == firstRun
            ? new AgentRunMonitor
            {
                ExecutionId = id,
                Status = AgentRunStatus.Failed,
                Error = new AgentRunError(AgentRunSubtypes.INTERNAL_ERROR),
            }
            : succeed(id);

        AgentConversationTurnOutcome outcome = await RunAsync(turn);

        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.Equal(2, _client.StartRequests.Count);
        Assert.Equal($"{PURPOSE}-{conversation.Id}-t1-p2", _client.StartRequests[1].ExecutionId);
        Assert.All(_client.StartRequests, request => Assert.Equal(AgentRunAdmissionClass.Chat, request.AdmissionClass));
    }

    /// <summary>
    /// The second turn continues the first turn's session: it carries the held bundle and the
    /// first turn's out/ files, and once it settles only its own bundle is kept.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_SecondTurn_ResumesTheFirstTurnsSession()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));

        AgentConversationTurnOutcome outcome = await RunAsync(await AddTurnAsync(conversation, 2, MODEL));

        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.True(outcome.ResumedSession);

        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Equal($"{PURPOSE}-{conversation.Id}-t2", second.ExecutionId);
        Assert.NotNull(second.Continuation);
        byte[] firstBundle = _bundles[$"{PURPOSE}-{conversation.Id}-t1"];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(firstBundle)), second.Continuation.SessionBundleSha256);
        AgentRunRecarriedFile outFile = Assert.Single(second.Continuation.OutFiles);
        Assert.Equal("notes.md", outFile.Path);
        Assert.Equal("message two", second.Prompt);

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentRunSessionBundle kept = await context.AgentRunSessionBundles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, kept.Round);
        Assert.True(await context.AgentConversationTurns.AllAsync(
            static row => row.Status == AgentConversationTurnStatus.Completed, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// With resuming off every turn starts fresh with the transcript, and no session
    /// bundle is bought, so nothing is billed for a continuation.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_ResumeOff_StartsFreshWithTheTranscript_AndKeepsNoBundle()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL), resume: false);

        AgentConversationTurnOutcome outcome = await RunAsync(await AddTurnAsync(conversation, 2, MODEL), resume: false);

        Assert.False(outcome.ResumedSession);
        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Null(second.Continuation);
        Assert.Contains("message one", second.Prompt);
        Assert.Contains($"answer of {PURPOSE}-{conversation.Id}-t1", second.Prompt);
        Assert.EndsWith("message two", second.Prompt);
        Assert.Empty(_client.CheckpointFetchedExecutionIds);

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Empty(await context.AgentRunSessionBundles.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A transcript belongs to the model that produced it, so a conversation switched to another
    /// model starts fresh with the earlier messages repeated in the prompt.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_ModelChanged_StartsFreshWithTheTranscript()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));

        AgentConversationTurnOutcome outcome = await RunAsync(await AddTurnAsync(conversation, 2, OTHER_MODEL));

        Assert.False(outcome.ResumedSession);
        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Null(second.Continuation);
        Assert.Equal(OTHER_MODEL, second.Model);
        Assert.Contains("message one", second.Prompt);
        Assert.Contains($"answer of {PURPOSE}-{conversation.Id}-t1", second.Prompt);
        Assert.EndsWith("message two", second.Prompt);
    }

    [Fact]
    public async Task RunTurnAsync_FailedRun_SettlesFailedWithAPlainReason()
    {
        _client.OnGetRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Failed,
            EstimatedCostUsd = COST_PER_RUN,
            Error = new AgentRunError(AgentRunSubtypes.BUDGET_EXHAUSTED),
        };
        AgentConversation conversation = await SeedConversationAsync(MODEL);

        AgentConversationTurnOutcome outcome = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));

        Assert.Equal(AgentConversationTurnStatus.Failed, outcome.Status);
        Assert.Contains("spending limit", outcome.Error);
        Assert.Equal(COST_PER_RUN, outcome.CostUsd);
    }

    /// <summary>
    /// A fault outside the run never escapes the runner and never leaves the turn in flight.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_ClientThrowsOnStart_SettlesTheTurnFailedInsteadOfThrowing()
    {
        _client.OnStartRun = _ => throw new InvalidOperationException("the runtime client broke");
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        AgentConversationTurnOutcome outcome = await RunAsync(turn);

        Assert.Equal(AgentConversationTurnStatus.Failed, outcome.Status);
        Assert.Equal(AgentConversationTurnStatus.Failed, (await ReadTurnAsync(turn.Id)).Status);
    }

    [Fact]
    public async Task RunTurnAsync_RuntimeHasChatSwitchedOff_FailsOnceWithAClearMessage()
    {
        _runtimeSettings.SendAdmissionClass = true;
        _runtimeSettings.MaxRunPasses = 3;
        _client.OnStartRun = _ => Error.Failure(AgentRunAdmissionErrors.CHAT_DISABLED, "chat admission is off");
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        AgentConversationTurnOutcome outcome = await RunAsync(turn);

        Assert.Equal(AgentConversationTurnStatus.Failed, outcome.Status);
        Assert.Equal("The agent is not available on this deployment.", outcome.Error);
        Assert.Single(_client.StartRequests);
    }

    [Fact]
    public async Task RunTurnAsync_TurnRowMissing_ReturnsFailedInsteadOfThrowing()
    {
        AgentConversationTurnOutcome outcome = await _provider.GetRequiredService<IAgentConversationRunner>()
            .RunTurnAsync(new AgentConversationTurnRequest("no-such-turn", "hello") { Billing = Billing() }, TestContext.Current.CancellationToken);

        Assert.Equal(AgentConversationTurnStatus.Failed, outcome.Status);
        Assert.Empty(_client.StartRequests);
    }

    /// <summary>
    /// Another party settled the turn Cancelled at no cost while its run went on: the run's settle
    /// leaves the verdict alone and only adds the cost the row was missing, billed once.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_TurnCancelledElsewhereAtNoCost_KeepsCancelledAndTakesTheRealCost()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        _client.OnStartRun = request =>
        {
            using IServiceScope scope = _provider.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            AgentConversationTurn row = context.AgentConversationTurns.Single(t => t.Id == turn.Id);
            row.Status = AgentConversationTurnStatus.Cancelled;
            row.CompletedAt = DateTime.UtcNow;
            _ = context.SaveChanges();
            return Running(request.ExecutionId);
        };

        AgentConversationTurnOutcome outcome = await RunAsync(turn);

        Assert.Equal(AgentConversationTurnStatus.Cancelled, outcome.Status);
        AgentConversationTurn settled = await ReadTurnAsync(turn.Id);
        Assert.Equal(AgentConversationTurnStatus.Cancelled, settled.Status);
        Assert.Null(settled.AnswerMarkdown);
        Assert.Equal(COST_PER_RUN, settled.CostUsd);

        using IServiceScope check = _provider.CreateScope();
        TokenUsage usage = await SpendRecordAsync(check.ServiceProvider.GetRequiredService<UserDataDbContext>());
        Assert.Equal(COST_PER_RUN, usage.Components.Single().Cost);
    }

    /// <summary>
    /// Stop cancels the run on the runtime, bills what it spent, and leaves the turn Cancelled.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_Cancelled_CancelsTheRunAndMarksTheTurnCancelled()
    {
        using var stop = new CancellationTokenSource();
        var cancelled = new Func<string, AgentRunMonitor>(id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Cancelled,
            EstimatedCostUsd = COST_PER_RUN,
        });
        _client.OnGetRun = id =>
        {
            if (stop.IsCancellationRequested)
            {
                return cancelled(id);
            }

            stop.Cancel();
            return Running(id);
        };
        _client.OnCancelRun = id => cancelled(id);
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        IAgentConversationRunner runner = _provider.GetRequiredService<IAgentConversationRunner>();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunTurnAsync(Request(turn), stop.Token));

        Assert.Contains($"{PURPOSE}-{conversation.Id}-t1", _client.CancelledExecutionIds);
        AgentConversationTurn settled = await ReadTurnAsync(turn.Id);
        Assert.Equal(AgentConversationTurnStatus.Cancelled, settled.Status);
        Assert.Equal(COST_PER_RUN, settled.CostUsd);
    }

    [Fact]
    public async Task RunTurnAsync_PublishesActivityUnderTheConversation()
    {
        var seen = new List<AgentRunActivityEntry>();
        IAgentRunActivityFeed feed = _provider.GetRequiredService<IAgentRunActivityFeed>();
        feed.EntryChanged += (_, entry) => seen.Add(entry);
        AgentConversation conversation = await SeedConversationAsync(MODEL);

        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));

        Assert.NotEmpty(seen);
        Assert.All(seen, entry => Assert.Equal(conversation.Id, entry.Registration.ConversationId));
        Assert.All(seen, static entry => Assert.Null(entry.Registration.WorkflowExecutionId));
        Assert.Contains(seen, static entry => entry.Settled);
        Assert.NotEmpty(feed.SnapshotForConversation(ORG_ID, conversation.Id));
        Assert.Empty(feed.SnapshotForConversation("another-org", conversation.Id));
    }

    /// <summary>
    /// Retention ends the idle conversation and deletes it whole in the same pass: turns by
    /// cascade, bundle rows with their bytes, and the runs' evidence rows by run id prefix.
    /// </summary>
    [Fact]
    public async Task SweepAsync_DeletesTheIdleConversationWithItsBundlesAndEvidence()
    {
        AgentConversation idle = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(idle, 1, MODEL));
        AgentConversation active = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(active, 1, MODEL));
        string idleBundleKey = await AgeAsync(idle.Id);

        int deleted = await Reaper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted);
        using IServiceScope after = _provider.CreateScope();
        UserDataDbContext remaining = after.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversation kept = await remaining.AgentConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(active.Id, kept.Id);
        Assert.Null(kept.EndedAt);
        Assert.DoesNotContain(
            await remaining.AgentRunTraceFiles.ToListAsync(TestContext.Current.CancellationToken),
            row => row.RunExecutionId.Contains(idle.Id, StringComparison.Ordinal));
        Assert.NotEmpty(await remaining.AgentRunTraceFiles.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await remaining.AgentConversationTurns.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(_storage.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, idleBundleKey));
    }

    /// <summary>
    /// A conversation still in use keeps its rows, but a turn settled before the retention window
    /// loses its runs' evidence: what the agent read must not outlive the window because the
    /// conversation stayed active.
    /// </summary>
    [Fact]
    public async Task SweepAsync_ActiveConversation_DropsTheEvidenceOfTurnsOlderThanTheWindowOnly()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn old = await AddTurnAsync(conversation, 1, MODEL);
        _ = await RunAsync(old);
        AgentConversationTurn recent = await AddTurnAsync(conversation, 2, MODEL);
        _ = await RunAsync(recent);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            AgentConversationTurn row = await context.AgentConversationTurns.SingleAsync(turn => turn.Id == old.Id, TestContext.Current.CancellationToken);
            row.CompletedAt = DateTime.UtcNow.AddDays(-8);
            row.ActivityJson = STEP_LABELS;
            (await context.AgentConversationTurns.SingleAsync(turn => turn.Id == recent.Id, TestContext.Current.CancellationToken)).ActivityJson = STEP_LABELS;
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        int deleted = await Reaper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, deleted);
        using IServiceScope after = _provider.CreateScope();
        UserDataDbContext remaining = after.ServiceProvider.GetRequiredService<UserDataDbContext>();
        List<string> runs = await remaining.AgentRunTraceFiles.Select(static row => row.RunExecutionId).Distinct().ToListAsync(TestContext.Current.CancellationToken);
        string oldRun = AgentRunIds.ForConversationTurn(conversation.Purpose, conversation.Id, 1, AgentRunIds.FIRST_PASS);
        string recentRun = AgentRunIds.ForConversationTurn(conversation.Purpose, conversation.Id, 2, AgentRunIds.FIRST_PASS);
        Assert.DoesNotContain(oldRun, runs);
        Assert.Contains(recentRun, runs);
        Assert.Equal(2, await remaining.AgentConversationTurns.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null((await remaining.AgentConversations.SingleAsync(TestContext.Current.CancellationToken)).EndedAt);
        Assert.Null((await remaining.AgentConversationTurns.SingleAsync(turn => turn.Id == old.Id, TestContext.Current.CancellationToken)).ActivityJson);
        Assert.Equal(STEP_LABELS, (await remaining.AgentConversationTurns.SingleAsync(turn => turn.Id == recent.Id, TestContext.Current.CancellationToken)).ActivityJson);
        Assert.NotNull((await remaining.AgentConversationTurns.SingleAsync(turn => turn.Id == old.Id, TestContext.Current.CancellationToken)).AnswerMarkdown);
    }

    /// <summary>
    /// A conversation idle past its window but holding a turn in flight, which is how a message
    /// sent at the moment of the sweep looks, is neither ended nor deleted.
    /// </summary>
    [Fact]
    public async Task SweepAsync_IdleConversationWithATurnInFlight_IsLeftLive()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        _ = await AddTurnAsync(conversation, 1, MODEL);
        _ = await AgeAsync(conversation.Id);

        int deleted = await Reaper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, deleted);
        using IServiceScope scope = _provider.CreateScope();
        AgentConversation row = await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .AgentConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(row.EndedAt);
    }

    /// <summary>
    /// A bundle whose bytes cannot be deleted keeps its row and its conversation; the next sweep,
    /// with storage back, deletes both.
    /// </summary>
    [Fact]
    public async Task SweepAsync_BundleBytesDeleteFails_KeepsTheConversationAndRetriesNextSweep()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        _ = await RunAsync(await AddTurnAsync(conversation, 1, MODEL));
        string bundleKey = await AgeAsync(conversation.Id);
        _storage.RefuseDeletes = true;

        int firstPass = await Reaper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, firstPass);
        using (IServiceScope scope = _provider.CreateScope())
        {
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            Assert.NotNull((await context.AgentConversations.SingleAsync(TestContext.Current.CancellationToken)).EndedAt);
            _ = await context.AgentRunSessionBundles.SingleAsync(TestContext.Current.CancellationToken);
        }

        Assert.NotNull(_storage.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, bundleKey));

        _storage.RefuseDeletes = false;
        int secondPass = await Reaper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, secondPass);
        Assert.Null(_storage.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, bundleKey));
    }

    /// <summary>
    /// Startup recovery settles only this host's turns: one with a run id, and one that started
    /// but had not reported a run id yet, whose first-pass run id is cancelled instead. The
    /// teardown's spend is recorded. Another host's turn and a waiting turn are left alone.
    /// </summary>
    [Fact]
    public async Task AbandonInFlightTurnsAsync_SettlesOnlyThisHostsTurns_AndBillsTheTeardown()
    {
        RuntimeReportsCancelledRuns();
        AgentConversation first = await SeedConversationAsync(MODEL);
        AgentConversation second = await SeedConversationAsync(MODEL);
        AgentConversation third = await SeedConversationAsync(MODEL);
        AgentConversationTurn running = await AddTurnAsync(first, 1, MODEL);
        AgentConversationTurn waiting = await AddTurnAsync(first, 2, MODEL);
        AgentConversationTurn starting = await AddTurnAsync(second, 1, MODEL);
        AgentConversationTurn elsewhere = await AddTurnAsync(third, 1, MODEL);
        await DispatchAsync(running.Id, ThisHost, $"{PURPOSE}-{first.Id}-t1", DateTime.UtcNow);
        await DispatchAsync(starting.Id, ThisHost, runId: null, DateTime.UtcNow);
        await DispatchAsync(elsewhere.Id, OTHER_HOST, $"{PURPOSE}-{third.Id}-t1", DateTime.UtcNow);

        int abandoned = await Reaper.AbandonInFlightTurnsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, abandoned);
        Assert.Contains($"{PURPOSE}-{first.Id}-t1", _client.CancelledExecutionIds);
        Assert.Contains($"{PURPOSE}-{second.Id}-t1", _client.CancelledExecutionIds);
        Assert.DoesNotContain($"{PURPOSE}-{third.Id}-t1", _client.CancelledExecutionIds);
        AgentConversationTurn settled = await ReadTurnAsync(running.Id);
        Assert.Equal(AgentConversationTurnStatus.Cancelled, settled.Status);
        Assert.Equal(TEARDOWN_COST, settled.CostUsd);
        Assert.Equal(AgentConversationTurnStatus.Queued, (await ReadTurnAsync(waiting.Id)).Status);
        Assert.Equal(AgentConversationTurnStatus.Running, (await ReadTurnAsync(elsewhere.Id)).Status);

        using IServiceScope scope = _provider.CreateScope();
        List<TokenUsage> billed = await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().TokenUsages
            .Include(static row => row.Components)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2 * TEARDOWN_COST, billed.SelectMany(static row => row.Components).Sum(static c => c.Cost));
    }

    /// <summary>
    /// A turn still in flight long past its deadline is failed whatever host ran it, its run
    /// cancelled and its spend recorded. A turn inside its deadline is left running.
    /// </summary>
    [Fact]
    public async Task SettleOverdueTurnsAsync_FailsTurnsPastTheirDeadline_OnAnyHost()
    {
        RuntimeReportsCancelledRuns();
        AgentConversation stale = await SeedConversationAsync(MODEL);
        AgentConversation fresh = await SeedConversationAsync(MODEL);
        AgentConversationTurn overdue = await AddTurnAsync(stale, 1, MODEL);
        AgentConversationTurn current = await AddTurnAsync(fresh, 1, MODEL);
        await DispatchAsync(overdue.Id, OTHER_HOST, $"{PURPOSE}-{stale.Id}-t1", DateTime.UtcNow.AddHours(-2));
        await DispatchAsync(current.Id, OTHER_HOST, $"{PURPOSE}-{fresh.Id}-t1", DateTime.UtcNow);

        int settled = await Reaper.SettleOverdueTurnsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, settled);
        AgentConversationTurn failed = await ReadTurnAsync(overdue.Id);
        Assert.Equal(AgentConversationTurnStatus.Failed, failed.Status);
        Assert.Equal(AgentConversationTurnErrors.OVERDUE, failed.Error);
        Assert.Equal(TEARDOWN_COST, failed.CostUsd);
        Assert.Contains($"{PURPOSE}-{stale.Id}-t1", _client.CancelledExecutionIds);
        Assert.Equal(AgentConversationTurnStatus.Running, (await ReadTurnAsync(current.Id)).Status);
    }

    /// <summary>The database itself refuses a bundle row with no owner.</summary>
    [Fact]
    public async Task SessionBundleRow_WithNeitherABlockNorAConversation_IsRefusedByTheDatabase()
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.AgentRunSessionBundles.Add(new AgentRunSessionBundle
        {
            OrganizationId = ORG_ID,
            Round = 1,
            RunExecutionId = "run-orphan",
            Sha256 = new string('a', 64),
            StorageProvider = "Database",
            StorageKey = "key",
        });

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("CHECK", refused.InnerException?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private IAgentConversationReaper Reaper => _provider.GetRequiredService<IAgentConversationReaper>();

    /// <summary>
    /// The consumer sends the agent on within the same turn: the second round continues the first
    /// round's session with the consumer's message, the turn answers with the last round, and the
    /// spend of both rounds lands on the one turn.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_ReviewAsksForAnotherRound_ContinuesTheSessionWithinTheSameTurn()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        var reviewed = new List<AgentConversationRound>();

        AgentConversationTurnOutcome outcome = await RunAsync(turn, resume: true, (round, _) =>
        {
            reviewed.Add(round);
            return Task.FromResult(round.Round == 1
                ? AgentConversationRoundReview.Continue(GATE_MESSAGE)
                : AgentConversationRoundReview.Finish);
        });

        string secondRun = $"{PURPOSE}-{conversation.Id}-t1-p2";
        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.Equal($"answer of {secondRun}", outcome.AnswerMarkdown);
        Assert.Equal(2 * COST_PER_RUN, outcome.CostUsd);

        Assert.Equal(2, _client.StartRequests.Count);
        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Equal(secondRun, second.ExecutionId);
        Assert.Equal(GATE_MESSAGE, second.Prompt);
        Assert.NotNull(second.Continuation);
        Assert.Equal("notes.md", Assert.Single(second.Continuation.OutFiles).Path);

        Assert.Equal([1, 2], reviewed.Select(static round => round.Round));
        Assert.Equal("notes.md", Assert.Single(reviewed[0].OutFiles).Path);
        Assert.False(reviewed[0].IsLastRound);

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationTurn settled = await context.AgentConversationTurns.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, settled.RunCount);
        Assert.Equal(secondRun, settled.RunExecutionId);
        Assert.Equal(2 * COST_PER_RUN, settled.CostUsd);
        Assert.Equal(2 * COST_PER_RUN, (await SpendRecordAsync(context)).Components.Single().Cost);
        AgentRunSessionBundle kept = await context.AgentRunSessionBundles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(secondRun, kept.RunExecutionId);
    }

    /// <summary>
    /// Without resumed sessions a further round starts fresh: the turn's message with the
    /// consumer's message after it, and no session bundle is fetched.
    /// </summary>
    [Fact]
    public async Task RunTurnAsync_ReviewAsksForAnotherRound_WithoutResumedSessions_StartsTheRoundFresh()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);

        var attempt = new AgentRunDocument("previous-attempt.json", Encoding.UTF8.GetBytes("{}"), "application/json");
        AgentConversationTurnOutcome outcome = await RunAsync(turn, resume: false, (round, _) =>
            Task.FromResult(round.Round == 1 ? AgentConversationRoundReview.Continue(GATE_MESSAGE, [attempt]) : AgentConversationRoundReview.Finish));

        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.Empty(_client.StartRequests[0].Documents);
        AgentRunStartRequest second = _client.StartRequests[1];
        Assert.Null(second.Continuation);
        Assert.Equal("previous-attempt.json", Assert.Single(second.Documents).Name);
        Assert.StartsWith(turn.UserMessage, second.Prompt, StringComparison.Ordinal);
        Assert.EndsWith(GATE_MESSAGE, second.Prompt, StringComparison.Ordinal);
        Assert.Empty(_client.CheckpointFetchedExecutionIds);
    }

    [Fact]
    public async Task RunTurnAsync_ReviewThatAlwaysContinues_StopsAtTheRoundCap_AndSaysTheLastRoundIsLast()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        var reviewed = new List<AgentConversationRound>();

        _ = await RunAsync(turn, resume: true, (round, _) =>
        {
            reviewed.Add(round);
            return Task.FromResult(AgentConversationRoundReview.Continue(GATE_MESSAGE));
        });

        Assert.Equal(3, _client.StartRequests.Count);
        Assert.Equal([false, false, true], reviewed.Select(static round => round.IsLastRound));
    }

    [Fact]
    public async Task RunTurnAsync_LessThanTheMinimumBudgetLeft_MakesItTheLastRound()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        AgentConversationRound? reviewed = null;

        _ = await _provider.GetRequiredService<IAgentConversationRunner>().RunTurnAsync(
            Request(turn) with
            {
                MaxBudgetUsd = COST_PER_RUN + 0.02m,
                MaxRounds = 3,
                ReviewRound = (round, _) =>
                {
                    reviewed = round;
                    return Task.FromResult(AgentConversationRoundReview.Continue(GATE_MESSAGE));
                },
            },
            TestContext.Current.CancellationToken);

        _ = Assert.Single(_client.StartRequests);
        Assert.True(reviewed!.IsLastRound);
    }

    [Fact]
    public async Task RunTurnAsync_LaterRoundFails_TheTurnKeepsThePreviousRoundsAnswer()
    {
        AgentConversation conversation = await SeedConversationAsync(MODEL);
        AgentConversationTurn turn = await AddTurnAsync(conversation, 1, MODEL);
        string secondRun = $"{PURPOSE}-{conversation.Id}-t1-p2";
        Func<string, ErrorOr<AgentRunMonitor>> succeed = _client.OnGetRun!;
        _client.OnGetRun = id => id == secondRun
            ? new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Failed, EstimatedCostUsd = COST_PER_RUN }
            : succeed(id);

        AgentConversationTurnOutcome outcome = await RunAsync(turn, resume: true, (round, _) =>
            Task.FromResult(AgentConversationRoundReview.Continue(GATE_MESSAGE)));

        Assert.Equal(AgentConversationTurnStatus.Completed, outcome.Status);
        Assert.Equal($"answer of {PURPOSE}-{conversation.Id}-t1", outcome.AnswerMarkdown);
        Assert.Equal(2 * COST_PER_RUN, outcome.CostUsd);
    }

    private void RuntimeSucceedsEveryRun()
    {
        byte[] trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");
        byte[] notes = Encoding.UTF8.GetBytes("notes the agent kept");

        _client.OnStartRun = request => Running(request.ExecutionId);
        _client.OnGetRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Succeeded,
            EstimatedCostUsd = COST_PER_RUN,
            Result = new AgentRunResult($"answer of {id}", "session"),
        };
        _client.OnCompleteRun = id => new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Succeeded };
        _client.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
        [
            Entry(TRACE_PATH, trace, AgentRunFileKind.Trace),
            Entry(OUT_PATH, notes, AgentRunFileKind.Artifact),
        ]);
        _client.OnGetRunFile = (_, path) => path == TRACE_PATH ? trace : notes;
        _client.OnGetRunCheckpoint = id =>
        {
            byte[] bundle = AgentRunSessionBundleContentTests.Bundle(
                AgentRunSessionBundleContentTests.ManifestJson(STAMP.Replace("\"x\"", $"\"{id}\"", StringComparison.Ordinal)));
            _bundles[id] = bundle;
            return new AgentRunCheckpoint(bundle, Convert.ToHexStringLower(SHA256.HashData(bundle)));
        };
    }

    private void RuntimeReportsCancelledRuns()
    {
        _client.OnCancelRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Cancelled,
            EstimatedCostUsd = TEARDOWN_COST,
        };
        _client.OnGetRun = id => new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Cancelled,
            EstimatedCostUsd = TEARDOWN_COST,
        };
    }

    private static AgentRunMonitor Running(string id)
    {
        return new AgentRunMonitor
        {
            ExecutionId = id,
            Status = AgentRunStatus.Running,
            PollInterval = TimeSpan.FromMilliseconds(5),
        };
    }

    private static AgentRunFileEntry Entry(string path, byte[] content, AgentRunFileKind kind)
    {
        return new AgentRunFileEntry
        {
            Path = path,
            SizeBytes = content.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            Kind = kind,
        };
    }

    private static async Task<TokenUsage> SpendRecordAsync(UserDataDbContext context)
    {
        return await context.TokenUsages
            .Include(static row => row.Components)
            .SingleAsync(
                row => row.Components.Any(c => c.ComponentType == PricingComponentType.AgentRunSpend),
                TestContext.Current.CancellationToken);
    }

    private async Task<AgentConversation> SeedConversationAsync(string model)
    {
        var conversation = new AgentConversation
        {
            OrganizationId = ORG_ID,
            UserId = USER_ID,
            Purpose = PURPOSE,
            SubjectId = "WorkflowType",
            ModelId = model,
            Effort = "medium",
        };

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.AgentConversations.Add(conversation);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return conversation;
    }

    private async Task<AgentConversationTurn> AddTurnAsync(AgentConversation conversation, int number, string model)
    {
        var turn = new AgentConversationTurn
        {
            ConversationId = conversation.Id,
            UserId = USER_ID,
            Number = number,
            UserMessage = number == 1 ? "message one" : "message two",
            ModelId = model,
        };

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.AgentConversationTurns.Add(turn);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return turn;
    }

    private async Task DispatchAsync(string turnId, string host, string? runId, DateTime dispatchedAt)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationTurn row = await context.AgentConversationTurns.SingleAsync(t => t.Id == turnId, TestContext.Current.CancellationToken);
        row.Status = runId is null ? AgentConversationTurnStatus.Queued : AgentConversationTurnStatus.Running;
        row.HostInstanceId = host;
        row.JobId = "job-of-a-stopped-process";
        row.DispatchedAt = dispatchedAt;
        row.RunExecutionId = runId;
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Moves the conversation's last activity past the retention window; returns its bundle's key, if any.</summary>
    private async Task<string> AgeAsync(string conversationId)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversation row = await context.AgentConversations.SingleAsync(c => c.Id == conversationId, TestContext.Current.CancellationToken);
        row.LastActivityAt = DateTime.UtcNow.AddDays(-8);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        AgentRunSessionBundle? bundle = await context.AgentRunSessionBundles
            .SingleOrDefaultAsync(b => b.ConversationId == conversationId, TestContext.Current.CancellationToken);

        return bundle?.StorageKey ?? string.Empty;
    }

    private async Task<AgentConversationTurn> ReadTurnAsync(string turnId)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await context.AgentConversationTurns.AsNoTracking().SingleAsync(row => row.Id == turnId, TestContext.Current.CancellationToken);
    }

    private async Task<AgentConversationTurnOutcome> RunAsync(AgentConversationTurn turn, bool resume = true)
    {
        return await _provider.GetRequiredService<IAgentConversationRunner>()
            .RunTurnAsync(Request(turn) with { ResumeSession = resume }, TestContext.Current.CancellationToken);
    }

    private async Task<AgentConversationTurnOutcome> RunAsync(
        AgentConversationTurn turn,
        bool resume,
        Func<AgentConversationRound, CancellationToken, Task<AgentConversationRoundReview>> review)
    {
        return await _provider.GetRequiredService<IAgentConversationRunner>().RunTurnAsync(
            Request(turn) with { ResumeSession = resume, ReviewRound = review, MaxRounds = 3 },
            TestContext.Current.CancellationToken);
    }

    private static AgentConversationTurnRequest Request(AgentConversationTurn turn)
    {
        return new AgentConversationTurnRequest(turn.Id, turn.UserMessage) { Billing = Billing() };
    }

    private static AgentConversationBilling Billing()
    {
        return new AgentConversationBilling(
            UsageType.LlmEditorAgentChat,
            TokenExecutionContext.WorkflowEditorAgent,
            "Workflow editor agent message");
    }

    /// <summary>
    /// A consumer of the shared layer for the test's purpose: a 15-minute turn, a week's retention.
    /// </summary>
    private sealed class ConversationTestConsumer : IAgentConversationConsumer
    {
        public string Purpose => PURPOSE;

        public AgentConversationPolicy Policy => new(7, 2, TimeSpan.FromMinutes(15));

        public AgentConversationBilling Billing(AgentConversation conversation)
        {
            return AgentConversationRunnerTests.Billing();
        }

        public Task<AgentConversationTurnRequest> StageTurnAsync(
            AgentConversationTurn turn,
            AgentConversation conversation,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Request(turn));
        }
    }

    /// <summary>
    /// The in-memory storage with a switch that makes deletes fail, the way an unreachable bucket does.
    /// </summary>
    private sealed class DeleteRefusingFileStorage : TestFileStorageService, IFileStorageService
    {
        public bool RefuseDeletes { get; set; }

        public new Task DeleteAsync(string organizationId, string category, string key)
        {
            return RefuseDeletes
                ? throw new IOException("storage is unreachable")
                : base.DeleteAsync(organizationId, category, key);
        }
    }
}
