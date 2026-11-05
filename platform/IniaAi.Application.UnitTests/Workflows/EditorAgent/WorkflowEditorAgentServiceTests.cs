using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Services.Jobs;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// The editor agent service end to end in-process: real job service and job executor, the real
/// shared conversation layer, runner and executor, real Identity roles, a SQLite UserData database
/// so cascades and conditional updates are the schema's own. Only the runtime's HTTP client is a
/// stand-in.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests : IAsyncDisposable
{
    private const string ORG_ID = "org-editor-agent";
    private const string OTHER_ORG_ID = "org-without-access";
    private const string USER_ID = "user-editor-agent";
    private const string OTHER_USER_ID = "user-someone-else";
    private const string PLAIN_USER_ID = "user-no-editor-role";
    private const string SUPER_ADMIN_ID = "user-super-admin";
    private const string OTHER_TYPE = "WT-Other";
    private const string THIRD_TYPE = "WT-Third";
    private const string SONNET = "us.anthropic.claude-sonnet-5";
    private const string HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string SONNET_5_5 = "us.anthropic.claude-sonnet-5-5";
    private const string HAIKU_5_5 = "us.anthropic.claude-haiku-5-5";
    private const string OPUS_5_5 = "us.anthropic.claude-opus-5-5";
    private const string GLOBAL_SONNET_5_5 = "global.anthropic.claude-sonnet-5-5";
    private const decimal COST_PER_RUN = 0.03m;
    private const int SETTLE_TIMEOUT_SECONDS = 30;

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly StubAgentRuntimeClient _client = new();
    private readonly TestFileStorageService _storage = new();
    private readonly ConcurrentQueue<EditorAgentTurnUpdate> _updates = new();
    private readonly EditorAgentSettings _editor = new() { Enabled = true, ResumeSessions = true };
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"editor-agent-{Guid.NewGuid():N}.db");
    private volatile bool _holdRuns;

    public WorkflowEditorAgentServiceTests()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        // A file database with a connection per context: turns run on job threads beside the
        // test's own reads, and one shared in-memory connection is not safe across threads.
        string userDataConnectionString = $"DataSource={_databasePath};Pooling=False";
        SqliteUserDataSchema.CreateFileDatabase(userDataConnectionString);
        DbContextOptions<UserDataDbContext> userDataOptions = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(userDataConnectionString).Options;
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(userDataOptions));
        _ = services.AddSingleton(userDataOptions);
        _ = services.AddSingleton<IDbContextFactory<UserDataDbContext>>(new SqliteUserDataDbContextFactory(userDataOptions));
        _ = services.AddTestDatabaseServicesWithoutDbContext();
        _ = services.AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>());
        DbContextOptions<CoreDbContext> coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"editor-agent-core-{Guid.NewGuid()}").Options;
        _ = services.AddScoped(_ => new CoreDbContext(coreOptions));
        _ = services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>();
        _ = services.AddApplication();
        _ = services.AddSingleton(Options.Create(new AgentRuntimeSettings
        {
            Enabled = true,
            AllowedModels = [HAIKU, SONNET],
            MaxRunPasses = 1,
        }));
        _ = services.AddSingleton(Options.Create(_editor));
        _ = services.AddSingleton(Options.Create(new AgentConversationSettings()));
        _ = services.AddSingleton<IAgentRunLimitsProvider, AgentRunLimitsCache>();
        _ = services.AddScoped<ITokenUsageService, TokenUsageService>();
        _ = services.AddSingleton<IFileStorageManager>(new FileStorageManager(
            new TestFileStorageResolver(_storage),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
        _ = services.AddSingleton<IAgentRuntimeClient>(_client);
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
        _ = services.AddTransient<IJobExecutor>(provider => new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: null!,
            emailIntakeJobStage: null!,
            runInputStage: null!));
        _ = services.AddSingleton<IJobsService>(provider => new JobsService(
            provider, NullLogger<JobsService>.Instance, Options.Create(new PlatformSettings())));
        _provider = services.BuildServiceProvider();

        Seed();
        _provider.GetRequiredService<IEditorAgentNotifications>().TurnUpdated += (_, update) => _updates.Enqueue(update);
        RuntimeAnswers();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _provider.GetRequiredService<IJobsService>().StopEveryJobAsync();
        }
        finally
        {
            try
            {
                await _provider.DisposeAsync();
            }
            finally
            {
                SqliteTestDatabaseFile.Delete(_databasePath);
            }
        }
    }

    private IWorkflowEditorAgentService Service => _provider.GetRequiredService<IWorkflowEditorAgentService>();

    [Fact]
    public async Task GetOrStartConversationAsync_StartsOnTheDefaultModel_AndReturnsTheSameOneAfterwards()
    {
        EditorAgentConversationView first = (await Service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        EditorAgentConversationView again = (await Service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(SONNET, first.ModelId);
        Assert.Equal("medium", first.Effort);
        Assert.Empty(first.Turns);
        Assert.Equal(first.LastActivityAt.AddDays(7), first.ExpiresAt);
    }

    [Fact]
    public async Task AnotherUser_CannotReadSendToOrCancelTheConversation()
    {
        EditorAgentConversationView mine = await StartAsync();

        ErrorOr<EditorAgentConversationView> read = await Service.GetConversationAsync(
            ORG_ID, OTHER_USER_ID, mine.Id, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentTurnView> sent = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, OTHER_USER_ID, mine.Id, "hello", SONNET, EditorAgentWorkflows.Build()),
            TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentConversationView> otherOrg = await Service.GetConversationAsync(
            "another-org", USER_ID, mine.Id, TestContext.Current.CancellationToken);

        Assert.Equal(AgentConversationErrors.NOT_FOUND_CODE, read.FirstError.Code);
        Assert.Equal(AgentConversationErrors.NOT_FOUND_CODE, sent.FirstError.Code);
        Assert.Equal(AgentConversationErrors.NOT_FOUND_CODE, otherOrg.FirstError.Code);
    }

    [Fact]
    public async Task GetOrStartConversationAsync_ChecksTheTypeTheRoleAndTheOrganizationsAccess()
    {
        ErrorOr<EditorAgentConversationView> unknownType = await Service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, "WT-Missing", TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentConversationView> noRole = await Service.GetOrStartConversationAsync(
            ORG_ID, PLAIN_USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentConversationView> noAccess = await Service.GetOrStartConversationAsync(
            OTHER_ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentConversationView> superAdmin = await Service.GetOrStartConversationAsync(
            OTHER_ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.WORKFLOW_TYPE_NOT_FOUND_CODE, unknownType.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, unknownType.FirstError.Type);
        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, noRole.FirstError.Code);
        Assert.Equal(ErrorType.Forbidden, noRole.FirstError.Type);
        Assert.Equal(EditorAgentErrors.FORBIDDEN_CODE, noAccess.FirstError.Code);
        Assert.False(superAdmin.IsError);
    }

    [Fact]
    public async Task SendMessageAsync_WorkflowOfAnotherType_IsRefused()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow other = EditorAgentWorkflows.Build();
        other.WorkflowTypeId = OTHER_TYPE;

        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, other);

        Assert.Equal(EditorAgentErrors.WORKFLOW_MISMATCH_CODE, sent.FirstError.Code);
        Assert.Empty(_client.StartRequests);
    }

    [Fact]
    public async Task SendMessageAsync_DraftOverTheSizeCap_IsRefusedBeforeAnythingIsStored()
    {
        _editor.MaxDraftMegabytes = 1;
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();
        ((AiQueryProperties)EditorAgentWorkflows.Reader(workflow).Properties).UserPromptTemplate = new string('x', 1_100_000);

        ErrorOr<EditorAgentTurnView> sent = await SendAsync(conversation.Id, workflow);

        Assert.Equal(EditorAgentErrors.DRAFT_TOO_LARGE_CODE, sent.FirstError.Code);
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Empty(await context.EditorAgentDrafts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AgentConversationTurns.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Two messages, as the panel sends them: each runs as a job, the first answers fresh, the
    /// second continues the first's session and is told which block the user edited in between.
    /// Both turns are billed under the editor agent's usage type.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_TwoTurns_SecondResumesTheSessionAndHearsWhatChanged()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();

        EditorAgentTurnView first = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "Where does the summary get revenue from?", SONNET, workflow),
            TestContext.Current.CancellationToken)).Value;
        Assert.False(first.IsWaiting);
        Assert.Equal(1, first.DraftRevision);
        await WaitForSettledAsync(conversation.Id, 1);

        ((AiQueryProperties)EditorAgentWorkflows.Reader(workflow).Properties).UserPromptTemplate = "Summarize in two lines: {revenue.answer}";
        EditorAgentTurnView second = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "And now?", SONNET, workflow),
            TestContext.Current.CancellationToken)).Value;
        Assert.Equal(2, second.DraftRevision);
        await WaitForSettledAsync(conversation.Id, 2);

        AgentRunStartRequest firstRun = _client.StartRequests[0];
        Assert.Equal($"edchat-{conversation.Id}-t1", firstRun.ExecutionId);
        Assert.Equal("Where does the summary get revenue from?", firstRun.Prompt);
        Assert.Equal(SONNET, firstRun.Model);
        Assert.Equal("medium", firstRun.Effort);
        Assert.Contains("Never save or run anything", firstRun.SystemPrompt);
        Assert.Equal(
            ["outline.txt", "workflow.json", "dependency-map.json", "versions.md", "reports.md"],
            firstRun.Documents.Select(static document => document.Name));
        Assert.Equal(EditorAgentStaging.SKILL_NAME, Assert.Single(firstRun.SkillFiles).Name);
        Assert.Equal(15 * 60 * 1000, firstRun.DeadlineMs);
        Assert.Equal(3m, firstRun.MaxBudgetUsd);

        AgentRunStartRequest secondRun = _client.StartRequests[1];
        Assert.NotNull(secondRun.Continuation);
        string readerId = EditorAgentWorkflows.Reader(workflow).StableBlockId!;
        Assert.StartsWith("And now?", secondRun.Prompt);
        Assert.Contains($"- changed: [{EditorAgentWorkflows.READER_NAME}](block:{readerId})", secondRun.Prompt);
        Assert.DoesNotContain(EditorAgentWorkflows.Producer(workflow).StableBlockId!, secondRun.Prompt);

        EditorAgentConversationView settled = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value;
        Assert.All(settled.Turns, turn => Assert.Equal(AgentConversationTurnStatus.Completed, turn.Status));
        Assert.Equal($"answer of edchat-{conversation.Id}-t2", settled.Turns[1].AnswerMarkdown);
        Assert.Equal(2 * COST_PER_RUN, settled.TotalCostUsd);
        Assert.NotNull(settled.Turns[0].Activity);

        // A turn shows as settled before its spend is recorded.
        await WaitUntilAsync(() => BilledChatTurns() == 2);
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        List<TokenUsage> billed = await context.TokenUsages
            .Where(static row => row.UsageType == UsageType.LlmEditorAgentChat)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, billed.Count);
        Assert.All(billed, row => Assert.Equal(USER_ID, row.UserId));
        Assert.All(billed, row => Assert.Equal(EditorAgentWorkflows.TYPE, row.WorkflowTypeId));
    }

    /// <summary>
    /// ResumeSessions off (the default): the second message starts a fresh session with
    /// the first exchange in its prompt, so the runtime bills it as a new run.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_ResumeSessionsOff_SecondTurnStartsFreshWithTheTranscript()
    {
        _editor.ResumeSessions = false;
        EditorAgentConversationView conversation = await StartAsync();

        _ = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());
        await WaitForSettledAsync(conversation.Id, 1);
        _ = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());
        await WaitForSettledAsync(conversation.Id, 2);

        AgentRunStartRequest secondRun = _client.StartRequests[1];
        Assert.Null(secondRun.Continuation);
        Assert.Contains($"answer of edchat-{conversation.Id}-t1", secondRun.Prompt);
        Assert.Empty(_client.CheckpointFetchedExecutionIds);
    }

    [Fact]
    public async Task SendMessageAsync_UnchangedWorkflow_ReusesTheDraftRow()
    {
        EditorAgentConversationView conversation = await StartAsync();
        Workflow workflow = EditorAgentWorkflows.Build();

        _ = await SendAsync(conversation.Id, workflow);
        await WaitForSettledAsync(conversation.Id, 1);
        EditorAgentTurnView second = (await SendAsync(conversation.Id, workflow)).Value;
        await WaitForSettledAsync(conversation.Id, 2);

        Assert.Equal(1, second.DraftRevision);
        Assert.EndsWith("The workflow has not changed since your last answer.", _client.StartRequests[1].Prompt);
        using IServiceScope scope = _provider.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .EditorAgentDrafts.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A message sent while the conversation is busy waits instead of being refused, and the
    /// waiting messages start in order once the ones before them settle. The per-user cap counts
    /// running messages only: two running and two waiting is allowed, a third start is not.
    /// </summary>
    [Fact]
    public async Task SendMessageAsync_WhileATurnRuns_WaitsAndStartsInOrder_AndTheCapCountsRunningTurnsOnly()
    {
        _holdRuns = true;
        EditorAgentConversationView first = await StartAsync();
        EditorAgentConversationView second = (await Service.StartNewConversationAsync(
            ORG_ID, USER_ID, OTHER_TYPE, null, TestContext.Current.CancellationToken)).Value;
        EditorAgentConversationView third = (await Service.StartNewConversationAsync(
            ORG_ID, SUPER_ADMIN_ID, EditorAgentWorkflows.TYPE, null, TestContext.Current.CancellationToken)).Value;

        EditorAgentTurnView one = (await SendAsync(first.Id, EditorAgentWorkflows.Build())).Value;
        EditorAgentTurnView two = (await SendAsync(first.Id, EditorAgentWorkflows.Build())).Value;
        EditorAgentTurnView three = (await SendAsync(first.Id, EditorAgentWorkflows.Build())).Value;
        Assert.False((await SendAsync(second.Id, WorkflowOf(OTHER_TYPE))).IsError);
        ErrorOr<EditorAgentTurnView> overCap = await SendAsync(
            (await Service.StartNewConversationAsync(ORG_ID, USER_ID, THIRD_TYPE, null, TestContext.Current.CancellationToken)).Value.Id,
            WorkflowOf(THIRD_TYPE));
        ErrorOr<EditorAgentTurnView> anotherUser = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, SUPER_ADMIN_ID, third.Id, "hello", SONNET, EditorAgentWorkflows.Build()),
            TestContext.Current.CancellationToken);

        Assert.False(one.IsWaiting);
        Assert.True(two.IsWaiting);
        Assert.True(three.IsWaiting);
        Assert.Equal(AgentConversationTurnStatus.Queued, two.Status);
        Assert.Equal(AgentConversationErrors.TOO_MANY_RUNNING_CODE, overCap.FirstError.Code);
        Assert.False(anotherUser.IsError);

        _holdRuns = false;
        await WaitForSettledAsync(first.Id, 3);

        List<string> order = [.. _client.StartRequests
            .Select(static request => request.ExecutionId)
            .Where(id => id.StartsWith($"edchat-{first.Id}-", StringComparison.Ordinal))];
        Assert.Equal([$"edchat-{first.Id}-t1", $"edchat-{first.Id}-t2", $"edchat-{first.Id}-t3"], order);
        EditorAgentConversationView view = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, first.Id, TestContext.Current.CancellationToken)).Value;
        Assert.All(view.Turns, turn => Assert.Equal(AgentConversationTurnStatus.Completed, turn.Status));
    }

    /// <summary>
    /// A waiting turn survives a restart: one left waiting after its predecessor settled, with
    /// nothing running to start it, is started by the host's dispatch sweep.
    /// </summary>
    [Fact]
    public async Task DispatchReadyAsync_StartsAWaitingTurnWhosePredecessorSettled()
    {
        EditorAgentConversationView conversation = await StartAsync();
        EditorAgentTurnView first = (await SendAsync(conversation.Id, EditorAgentWorkflows.Build())).Value;
        await WaitForSettledAsync(conversation.Id, 1);
        // The first turn's job starts waiting turns as it ends, so the seed waits for that job to end.
        await WaitUntilAsync(() => JobOfTurnEnded(first.Id));
        await SeedWaitingTurnAsync(conversation.Id, first.Id, 2);

        int started = await _provider.GetRequiredService<IAgentConversationTurns>()
            .DispatchReadyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, started);
        await WaitForSettledAsync(conversation.Id, 2);
        Assert.Contains(_client.StartRequests, request => request.ExecutionId == $"edchat-{conversation.Id}-t2");
    }

    [Fact]
    public async Task CancelTurnAsync_WaitingTurn_IsCancelledAndNeverStarts()
    {
        _holdRuns = true;
        EditorAgentConversationView conversation = await StartAsync();
        _ = await SendAsync(conversation.Id, EditorAgentWorkflows.Build());
        EditorAgentTurnView waiting = (await SendAsync(conversation.Id, EditorAgentWorkflows.Build())).Value;

        ErrorOr<Success> cancelled = await Service.CancelTurnAsync(ORG_ID, USER_ID, waiting.Id, TestContext.Current.CancellationToken);
        _holdRuns = false;
        await WaitForSettledAsync(conversation.Id, 1);

        Assert.False(cancelled.IsError);
        EditorAgentConversationView view = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value;
        Assert.Equal(AgentConversationTurnStatus.Cancelled, view.Turns[1].Status);
        Assert.DoesNotContain(_client.StartRequests, request => request.ExecutionId == $"edchat-{conversation.Id}-t2");
    }

    /// <summary>
    /// Stop pressed twice: the job is this host's for as long as the job service lists it, so the
    /// second press does not mark the turn Cancelled at no cost while its run is still settling.
    /// </summary>
    [Fact]
    public async Task CancelTurnAsync_PressedTwice_StopsTheRunAndBillsIt()
    {
        _holdRuns = true;
        int polls = 0;
        Func<string, ErrorOr<AgentRunMonitor>> answers = _client.OnGetRun!;
        _client.OnGetRun = id =>
        {
            _ = Interlocked.Increment(ref polls);
            return answers(id);
        };
        EditorAgentConversationView conversation = await StartAsync();
        EditorAgentTurnView turn = (await SendAsync(conversation.Id, EditorAgentWorkflows.Build())).Value;
        // Stop once the run is being polled: a stop while the turn still records the start bills nothing.
        await WaitUntilAsync(() => Volatile.Read(ref polls) > 0);

        ErrorOr<Success> first = await Service.CancelTurnAsync(ORG_ID, USER_ID, turn.Id, TestContext.Current.CancellationToken);
        ErrorOr<Success> second = await Service.CancelTurnAsync(ORG_ID, USER_ID, turn.Id, TestContext.Current.CancellationToken);
        await WaitForSettledAsync(conversation.Id, 1);

        Assert.False(first.IsError);
        Assert.False(second.IsError);
        Assert.Contains($"edchat-{conversation.Id}-t1", _client.CancelledExecutionIds);
        EditorAgentTurnView settled = Assert.Single((await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value.Turns);
        Assert.Equal(AgentConversationTurnStatus.Cancelled, settled.Status);
        Assert.Equal(COST_PER_RUN, settled.CostUsd);
    }

    /// <summary>
    /// A turn stopped at its deadline settles before the run's teardown reports what it cost; the
    /// later, higher cost reaches the open panel as an update, so its cost line reads it again.
    /// </summary>
    [Fact]
    public async Task ACostLearnedAfterTheTurnSettled_IsAnUpdateTheOpenPanelReceives()
    {
        EditorAgentConversationView conversation = await StartAsync();
        EditorAgentTurnView turn = (await SendAsync(conversation.Id, EditorAgentWorkflows.Build())).Value;
        await WaitForSettledAsync(conversation.Id, 1);
        _updates.Clear();

        AgentConversationSettlement raised = await _provider.GetRequiredService<AgentConversationLedger>().SettleAsync(
            turn.Id, new AgentConversationVerdict(AgentConversationTurnStatus.Cancelled, null, 5 * COST_PER_RUN, null));

        Assert.True(raised.Written);
        Assert.Contains(_updates, update => update.ConversationId == conversation.Id && update.TurnNumber == 1);
        Assert.Equal(5 * COST_PER_RUN, (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value.TotalCostUsd);
    }

    /// <summary>
    /// A turn another host runs is stopped through the runtime by its run id and left for that host
    /// to settle; one with no run id yet is refused, never marked Cancelled while it may still run.
    /// </summary>
    [Fact]
    public async Task CancelTurnAsync_TurnOfAnotherHost_CancelsTheRunOrRefuses_ButNeverMarksIt()
    {
        EditorAgentConversationView conversation = await StartAsync();
        string withRun = await SeedTurnOfAnotherHostAsync(conversation.Id, 1, $"edchat-{conversation.Id}-t1");
        string starting = await SeedTurnOfAnotherHostAsync(conversation.Id, 2, runId: null);

        ErrorOr<Success> cancelled = await Service.CancelTurnAsync(ORG_ID, USER_ID, withRun, TestContext.Current.CancellationToken);
        ErrorOr<Success> refused = await Service.CancelTurnAsync(ORG_ID, USER_ID, starting, TestContext.Current.CancellationToken);

        Assert.False(cancelled.IsError);
        Assert.Contains($"edchat-{conversation.Id}-t1", _client.CancelledExecutionIds);
        Assert.Equal(AgentConversationErrors.STARTING_ELSEWHERE_CODE, refused.FirstError.Code);
        EditorAgentConversationView view = (await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversation.Id, TestContext.Current.CancellationToken)).Value;
        Assert.All(view.Turns, turn => Assert.True(turn.Status.IsInFlight()));
    }

    /// <summary>
    /// New conversation ends the old one: it takes no more messages, and the next retention
    /// sweep deletes it with its turns and drafts.
    /// </summary>
    [Fact]
    public async Task StartNewConversationAsync_EndsTheOldOne_WhichTheNextSweepDeletes()
    {
        EditorAgentConversationView old = await StartAsync();
        _ = await SendAsync(old.Id, EditorAgentWorkflows.Build());
        await WaitForSettledAsync(old.Id, 1);

        EditorAgentConversationView fresh = (await Service.StartNewConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, null, TestContext.Current.CancellationToken)).Value;
        ErrorOr<EditorAgentTurnView> late = await SendAsync(old.Id, EditorAgentWorkflows.Build());
        int deleted = await _provider.GetRequiredService<IAgentConversationReaper>().SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AgentConversationErrors.NOT_FOUND_CODE, late.FirstError.Code);
        Assert.Equal(1, deleted);
        Assert.Equal(fresh.Id, (await StartAsync()).Id);
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.Equal(fresh.Id, (await context.AgentConversations.SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Empty(await context.AgentConversationTurns.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.EditorAgentDrafts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AgentRunSessionBundles.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AgentRunTraceFiles.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendMessageAsync_ModelOutsideTheRuntimeAllowlist_IsRefused()
    {
        EditorAgentConversationView conversation = await StartAsync();

        ErrorOr<EditorAgentTurnView> sent = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "hello", "gpt-5", EditorAgentWorkflows.Build()),
            TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.MODEL_NOT_ALLOWED_CODE, sent.FirstError.Code);
    }

    /// <summary>
    /// A model priced under two providers shows the price of the provider its allowlist id runs
    /// through: a Bedrock inference-profile id takes the Bedrock row, even when the Azure AI
    /// Foundry row is newer.
    /// </summary>
    [Fact]
    public async Task ListModelsAsync_PricesEachModelFromItsOwnProvidersRow_AndMarksSonnetDefault()
    {
        using (IServiceScope scope = _provider.CreateScope())
        {
            CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            _ = core.Add(Pricing("aws-bedrock", "Claude Sonnet 5", 3m, 15m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            _ = core.Add(Pricing("azure-foundry", "Claude Sonnet 5 (Azure AI Foundry)", 2m, 10m, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
            _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IReadOnlyList<EditorAgentModelOption> models = await Service.ListModelsAsync(ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([HAIKU, SONNET], models.Select(static model => model.ModelId));
        EditorAgentModelOption sonnet = Assert.Single(models, static model => model.IsDefault);
        Assert.Equal(SONNET, sonnet.ModelId);
        Assert.Equal("Claude Sonnet 5", sonnet.DisplayName);
        Assert.Equal(3m, sonnet.InputUsdPerMillionTokens);
        Assert.Equal(15m, sonnet.OutputUsdPerMillionTokens);
        Assert.Contains("most questions", sonnet.Capability);
        Assert.Null(models[0].InputUsdPerMillionTokens);
    }

    [Fact]
    public async Task Disabled_HidesTheFeature()
    {
        var disabled = new WorkflowEditorAgentService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IAgentConversationTurns>(),
            Options.Create(new AgentRuntimeSettings { Enabled = false, AllowedModels = [SONNET] }),
            Options.Create(new EditorAgentSettings { Enabled = true }));

        Assert.False(disabled.IsEnabled);
        ErrorOr<EditorAgentConversationView> started = await disabled.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken);
        Assert.Equal(EditorAgentErrors.DISABLED_CODE, started.FirstError.Code);
        Assert.Empty(await disabled.ListModelsAsync(ORG_ID, cancellationToken: TestContext.Current.CancellationToken));
    }

    private void Seed()
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.Organizations.Add(new Organization("Editor agent organization", ORG_ID));
        _ = context.Organizations.Add(new Organization("Organization without access", OTHER_ORG_ID));
        var editorRole = new IdentityRole(Roles.WORKFLOW_EDITOR) { NormalizedName = Roles.WORKFLOW_EDITOR.ToUpperInvariant() };
        var superAdminRole = new IdentityRole(Roles.SUPER_ADMIN) { NormalizedName = Roles.SUPER_ADMIN.ToUpperInvariant() };
        _ = context.Roles.Add(editorRole);
        _ = context.Roles.Add(superAdminRole);
        foreach (string userId in new[] { USER_ID, OTHER_USER_ID, PLAIN_USER_ID, SUPER_ADMIN_ID })
        {
            _ = context.Users.Add(new User
            {
                Id = userId,
                OrganizationId = ORG_ID,
                UserName = userId,
                FirstName = "Editor",
                LastName = "User",
            });
        }

        _ = context.UserRoles.Add(new IdentityUserRole<string> { UserId = USER_ID, RoleId = editorRole.Id });
        _ = context.UserRoles.Add(new IdentityUserRole<string> { UserId = OTHER_USER_ID, RoleId = editorRole.Id });
        _ = context.UserRoles.Add(new IdentityUserRole<string> { UserId = SUPER_ADMIN_ID, RoleId = superAdminRole.Id });
        _ = context.SaveChanges();

        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        foreach ((string typeId, string name) in new[] { (EditorAgentWorkflows.TYPE, TYPE_NAME), (OTHER_TYPE, OTHER_TYPE_NAME), (THIRD_TYPE, THIRD_TYPE) })
        {
            _ = core.Add(new WorkflowType { Id = typeId, Name = name });
            _ = core.Add(new WorkflowTypeOrganizationAccess { WorkflowTypeId = typeId, OrganizationId = ORG_ID });
        }

        _ = core.SaveChanges();
    }

    private async Task<EditorAgentConversationView> StartAsync()
    {
        return (await Service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
    }

    private Task<ErrorOr<EditorAgentTurnView>> SendAsync(string conversationId, Workflow workflow)
    {
        return Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversationId, "What does this workflow do?", SONNET, workflow),
            TestContext.Current.CancellationToken);
    }

    private static Workflow WorkflowOf(string workflowTypeId)
    {
        Workflow workflow = EditorAgentWorkflows.Build();
        workflow.WorkflowTypeId = workflowTypeId;
        return workflow;
    }

    private async Task SeedWaitingTurnAsync(string conversationId, string previousTurnId, int number)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        AgentConversationTurn previous = await context.AgentConversationTurns
            .AsNoTracking()
            .SingleAsync(row => row.Id == previousTurnId, TestContext.Current.CancellationToken);
        _ = context.AgentConversationTurns.Add(new AgentConversationTurn
        {
            ConversationId = conversationId,
            UserId = USER_ID,
            Number = number,
            UserMessage = "sent while the first was running",
            ModelId = SONNET,
            ContextRef = previous.ContextRef,
        });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> SeedTurnOfAnotherHostAsync(string conversationId, int number, string? runId)
    {
        var turn = new AgentConversationTurn
        {
            ConversationId = conversationId,
            UserId = USER_ID,
            Number = number,
            UserMessage = "running elsewhere",
            ModelId = SONNET,
            Status = runId is null ? AgentConversationTurnStatus.Queued : AgentConversationTurnStatus.Running,
            RunExecutionId = runId,
            JobId = "job-on-another-host",
            HostInstanceId = "another-host/IniaAi.WebApp",
            DispatchedAt = DateTime.UtcNow,
        };

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.AgentConversationTurns.Add(turn);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return turn.Id;
    }

    /// <summary>
    /// The picker offers Opus 5.5 first as the expert model, then the platform's two default
    /// models as the deployment resolves them. The deployment calls the tiers by their public API
    /// ids, and the allowlist's Bedrock form of the same model is the one offered. The advanced
    /// model stays the default.
    /// </summary>
    [Fact]
    public async Task ListModelsAsync_OffersOpusAsExpertFirst_TheAdvancedModelAsDefault_AndTheBasicModelAsStandard()
    {
        WorkflowEditorAgentService service = TieredService([HAIKU, SONNET, SONNET_5_5, HAIKU_5_5, OPUS_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([OPUS_5_5, SONNET_5_5, HAIKU_5_5], models.Select(static model => model.ModelId));
        Assert.Equal(
            [EditorAgentModelCatalog.EXPERT_TIER, EditorAgentModelCatalog.ADVANCED_TIER, EditorAgentModelCatalog.STANDARD_TIER],
            models.Select(static model => model.Tier));
        Assert.Equal(SONNET_5_5, Assert.Single(models, static model => model.IsDefault).ModelId);
    }

    [Fact]
    public async Task ListModelsAsync_LeavesOutTheExpertModel_WhenTheRuntimeDoesNotAllowIt()
    {
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, HAIKU_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([SONNET_5_5, HAIKU_5_5], models.Select(static model => model.ModelId));
    }

    /// <summary>The expert model's allowlist form under another prefix is the one offered.</summary>
    [Fact]
    public async Task ListModelsAsync_OffersTheAllowlistsFormOfTheExpertModel()
    {
        const string globalOpus = "global.anthropic.claude-opus-5-5";
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, HAIKU_5_5, globalOpus]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([globalOpus, SONNET_5_5, HAIKU_5_5], models.Select(static model => model.ModelId));
    }

    [Fact]
    public async Task ListModelsAsync_AnEmptyExpertModel_HidesTheSlot()
    {
        _editor.ExpertModel = string.Empty;
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, HAIKU_5_5, OPUS_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([SONNET_5_5, HAIKU_5_5], models.Select(static model => model.ModelId));
    }

    [Fact]
    public async Task StartNewConversationAsync_AcceptsTheExpertModel()
    {
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, HAIKU_5_5, OPUS_5_5]);

        ErrorOr<EditorAgentConversationView> started = await service.StartNewConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, OPUS_5_5, TestContext.Current.CancellationToken);

        Assert.Equal(OPUS_5_5, started.Value.ModelId);
    }

    [Fact]
    public async Task ListModelsAsync_FollowsTheOrganizationsModelOverride()
    {
        WorkflowEditorAgentService service = TieredService(
            [SONNET_5_5, HAIKU_5_5, OPUS_5_5],
            settings => settings.ModelRouting.TenantModelOverrides[ORG_ID] = new TenantModelOverride { AdvancedModel = "claude-opus-5-5" });

        IReadOnlyList<EditorAgentModelOption> overridden = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<EditorAgentModelOption> other = await service.ListModelsAsync(
            OTHER_ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([OPUS_5_5, HAIKU_5_5], overridden.Select(static model => model.ModelId));
        Assert.Equal(EditorAgentModelCatalog.ADVANCED_TIER, overridden[0].Tier);
        Assert.Equal([OPUS_5_5, SONNET_5_5, HAIKU_5_5], other.Select(static model => model.ModelId));
    }

    /// <summary>A settings override, such as a Foundry deployment name, replaces the tier's model as it does on every call.</summary>
    [Fact]
    public async Task ListModelsAsync_FollowsTheDeploymentsModelIdOverride()
    {
        const string deployment = "inia-sonnet-5-5";
        WorkflowEditorAgentService service = TieredService(
            [deployment, SONNET_5_5, HAIKU_5_5],
            settings => settings.Anthropic.AdvancedModelIdOverride = deployment);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([deployment, HAIKU_5_5], models.Select(static model => model.ModelId));
    }

    [Fact]
    public async Task ListModelsAsync_LeavesOutADefaultTheRuntimeDoesNotAllow()
    {
        WorkflowEditorAgentService service = TieredService([HAIKU, SONNET_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        EditorAgentModelOption advanced = Assert.Single(models);
        Assert.Equal(SONNET_5_5, advanced.ModelId);
        Assert.True(advanced.IsDefault);
    }

    [Fact]
    public async Task ListModelsAsync_WhenTheRuntimeAllowsNeitherDefault_OffersEveryAllowedModel()
    {
        WorkflowEditorAgentService service = TieredService([HAIKU, SONNET]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([HAIKU, SONNET], models.Select(static model => model.ModelId));
        Assert.All(models, static model => Assert.Null(model.Tier));
        Assert.Equal(SONNET, Assert.Single(models, static model => model.IsDefault).ModelId);
    }

    [Fact]
    public async Task ListModelsAsync_KeepsAConversationsOlderModelListed_ButNotAsADefault()
    {
        WorkflowEditorAgentService service = TieredService([SONNET, SONNET_5_5, HAIKU_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, SONNET, TestContext.Current.CancellationToken);

        Assert.Equal([SONNET_5_5, HAIKU_5_5, SONNET], models.Select(static model => model.ModelId));
        EditorAgentModelOption older = models[2];
        Assert.Null(older.Tier);
        Assert.False(older.IsDefault);
    }

    /// <summary>A conversation on another prefix of a default model is not listed a second time.</summary>
    [Fact]
    public async Task ListModelsAsync_ListsAModelOnceAcrossItsUsAndGlobalForms()
    {
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, GLOBAL_SONNET_5_5, HAIKU_5_5]);

        IReadOnlyList<EditorAgentModelOption> models = await service.ListModelsAsync(
            ORG_ID, GLOBAL_SONNET_5_5, TestContext.Current.CancellationToken);

        Assert.Equal([SONNET_5_5, HAIKU_5_5], models.Select(static model => model.ModelId));
    }

    [Fact]
    public async Task StartNewConversationAsync_RefusesAModelOutsideTheOrganizationsTiers()
    {
        WorkflowEditorAgentService service = TieredService(
            [SONNET_5_5, HAIKU_5_5, OPUS_5_5],
            settings => settings.ModelRouting.TenantModelOverrides[ORG_ID] = new TenantModelOverride { AdvancedModel = "claude-haiku-5-5" });

        ErrorOr<EditorAgentConversationView> sonnet = await service.StartNewConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, SONNET_5_5, TestContext.Current.CancellationToken);
        ErrorOr<EditorAgentConversationView> haiku = await service.StartNewConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, HAIKU_5_5, TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.MODEL_NOT_ALLOWED_CODE, sonnet.FirstError.Code);
        Assert.Equal(HAIKU_5_5, haiku.Value.ModelId);
    }

    [Fact]
    public async Task SendMessageAsync_RefusesAnAllowedModelOutsideTheOrganizationsTiers()
    {
        WorkflowEditorAgentService service = TieredService([SONNET_5_5, HAIKU_5_5, SONNET]);
        EditorAgentConversationView conversation = (await service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        ErrorOr<EditorAgentTurnView> sent = await service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "hello", SONNET, EditorAgentWorkflows.Build()),
            TestContext.Current.CancellationToken);

        Assert.Equal(EditorAgentErrors.MODEL_NOT_ALLOWED_CODE, sent.FirstError.Code);
    }

    /// <summary>
    /// A conversation started when the advanced model was Sonnet 5 keeps it; a new conversation
    /// after the advanced model moved starts on the new one.
    /// </summary>
    [Fact]
    public async Task ANewConversation_AfterOneOnAnOlderModel_StartsOnTheAdvancedModel()
    {
        string[] allowed = [SONNET, SONNET_5_5, HAIKU_5_5];
        WorkflowEditorAgentService before = TieredService(allowed, settings => settings.Anthropic.AdvancedModel = AiModel.UsAwsSonnet5);
        EditorAgentConversationView older = (await before.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;
        WorkflowEditorAgentService after = TieredService(allowed);

        IReadOnlyList<EditorAgentModelOption> kept = await after.ListModelsAsync(ORG_ID, older.ModelId, TestContext.Current.CancellationToken);
        EditorAgentConversationView fresh = (await after.StartNewConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, modelId: null, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(SONNET, older.ModelId);
        Assert.Contains(kept, model => model.ModelId == SONNET && model.Tier is null);
        Assert.Equal(SONNET_5_5, fresh.ModelId);
    }

    [Fact]
    public async Task ANewConversation_StartsOnTheAdvancedModel()
    {
        WorkflowEditorAgentService service = TieredService([SONNET, SONNET_5_5, HAIKU_5_5]);

        EditorAgentConversationView conversation = (await service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentWorkflows.TYPE, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(SONNET_5_5, conversation.ModelId);
    }

    /// <summary>
    /// The service over a real public API connector, which makes no call here, whose advanced
    /// model is Sonnet 5.5 and whose basic model is Haiku 5.5.
    /// </summary>
    private WorkflowEditorAgentService TieredService(string[] allowedModels, Action<PlatformSettings>? configure = null)
    {
        var settings = new PlatformSettings();
        settings.Anthropic.ApiKey = "not-used";
        settings.Anthropic.AdvancedModel = AiModel.PublicSonnet5_5;
        settings.Anthropic.BasicModel = AiModel.PublicHaiku5_5;
        configure?.Invoke(settings);

        return new WorkflowEditorAgentService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IAgentConversationTurns>(),
            Options.Create(new AgentRuntimeSettings { Enabled = true, AllowedModels = [.. allowedModels] }),
            Options.Create(_editor),
            chatConnector: new AnthropicChatConnector(
                settings,
                NullLogger<AnthropicChatConnector>.Instance,
                new NoOpLimiter()),
            tenantModels: new TenantModelFamilyResolver(Options.Create(settings), NullLogger<TenantModelFamilyResolver>.Instance));
    }

    private static ModelPricing Pricing(string provider, string displayName, decimal input, decimal output, DateTime effectiveFrom)
    {
        var pricing = new ModelPricing
        {
            Provider = provider,
            ModelId = "claude-sonnet-5",
            DisplayName = displayName,
            PricingType = PricingType.Llm,
            IsActive = true,
            EffectiveFrom = effectiveFrom,
        };
        pricing.Components.Add(new ModelPricingComponent { ComponentType = PricingComponentType.Input, Cost = input, Unit = PricingUnit.PerMillion });
        pricing.Components.Add(new ModelPricingComponent { ComponentType = PricingComponentType.Output, Cost = output, Unit = PricingUnit.PerMillion });

        return pricing;
    }

    private async Task WaitForSettledAsync(string conversationId, int turnNumber)
    {
        await WaitUntilAsync(() => _updates.Any(update => update.ConversationId == conversationId
            && update.TurnNumber == turnNumber
            && update.Status is AgentConversationTurnStatus.Completed
                or AgentConversationTurnStatus.Failed
                or AgentConversationTurnStatus.Cancelled));
    }

    private bool JobOfTurnEnded(string turnId)
    {
        using IServiceScope scope = _provider.CreateScope();
        string? jobId = scope.ServiceProvider.GetRequiredService<UserDataDbContext>().AgentConversationTurns
            .AsNoTracking()
            .Where(row => row.Id == turnId)
            .Select(static row => row.JobId)
            .Single();
        return jobId is null || _provider.GetRequiredService<IJobsService>().GetJob(jobId) is not { IsFinished: false };
    }

    private int BilledChatTurns()
    {
        using IServiceScope scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<UserDataDbContext>().TokenUsages
            .Count(static row => row.UsageType == UsageType.LlmEditorAgentChat);
    }

    private static Task WaitUntilAsync(Func<bool> condition)
    {
        return Eventually.UntilAsync(condition, "the turn", TimeSpan.FromSeconds(SETTLE_TIMEOUT_SECONDS), cancellationToken: TestContext.Current.CancellationToken);
    }

    private void RuntimeAnswers()
    {
        byte[] trace = Encoding.UTF8.GetBytes("""{"type":"stage","stage":"started","ts":"2026-10-06T10:00:00Z"}""" + "\n");

        _client.OnStartRun = request => Running(request.ExecutionId);
        _client.OnGetRun = id => _holdRuns
            ? Running(id)
            : new AgentRunMonitor
            {
                ExecutionId = id,
                Status = AgentRunStatus.Succeeded,
                EstimatedCostUsd = COST_PER_RUN,
                Result = new AgentRunResult($"answer of {id}", "session"),
            };
        _client.OnCancelRun = id =>
        {
            _holdRuns = false;
            return new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Cancelled, EstimatedCostUsd = COST_PER_RUN };
        };
        _client.OnCompleteRun = id => new AgentRunMonitor { ExecutionId = id, Status = AgentRunStatus.Succeeded };
        _client.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
        [
            new AgentRunFileEntry
            {
                Path = "trace/trace.ndjson",
                SizeBytes = trace.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(trace)),
                Kind = AgentRunFileKind.Trace,
            },
        ]);
        _client.OnGetRunFile = (_, _) => trace;
        _client.OnGetRunCheckpoint = id =>
        {
            string stamp = $$"""{"bundleFormat":"1","sdk":"2.0.44","model":"{{SONNET}}","run":"{{id}}"}""";
            byte[] bundle = IniaAi.Domain.Serialization.WorkflowJsonCompression.Compress($$"""{"sessionId":"s","files":{},"version":{{stamp}}}""");
            return new AgentRunCheckpoint(bundle, Convert.ToHexStringLower(SHA256.HashData(bundle)));
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
}
