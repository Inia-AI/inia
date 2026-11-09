using System.Collections.Concurrent;
using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
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

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals;

/// <summary>
/// The editor agent service as the editor runs it, against a live agent runtime: the same wiring
/// as the service tests, with the real runtime client in place of the stand-in. One host per
/// eval case, so no case sees another's conversation.
/// </summary>
internal sealed class EditorAgentEvalHost : IAsyncDisposable
{
    public const string ORG_ID = "org-editor-agent-evals";
    public const string USER_ID = "user-editor-agent-evals";
    public const string TYPE_NAME = "Editor agent eval workflow";

    private const string BASE_URL_ENV = "INIA_TEST_AGENT_RUNTIME";
    private const string MODEL_ENV = "INIA_TEST_EDITOR_AGENT_EVAL_MODEL";
    private const string CASES_ENV = "INIA_TEST_EDITOR_AGENT_EVAL_CASES";
    private const string EVALS_ENV = "INIA_TEST_EDITOR_AGENT_EVALS";
    private const string OPTED_IN = "1";
    private const string DEFAULT_MODEL = "us.anthropic.claude-sonnet-5";
    private const int TURN_TIMEOUT_MINUTES = 16;

    // A cancelled live turn winds down within AgentRunExecutor's own limits: 10 s to cancel the run,
    // about 7 s to see it settle, then 30 s each to collect, persist and take custody of its files.
    private static readonly TimeSpan _stopJobsTimeout = TimeSpan.FromMinutes(2);

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly ConcurrentQueue<EditorAgentTurnUpdate> _updates = new();
    private readonly EditorAgentEvalStartRecorder _starts = new(new HttpClientHandler());
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"editor-agent-eval-{Guid.NewGuid():N}.db");

    public EditorAgentEvalHost(string baseUrl, string model)
    {
        Model = model;
        var services = new ServiceCollection();
        _ = services.AddLogging();
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
            .UseInMemoryDatabase($"editor-agent-eval-core-{Guid.NewGuid()}").Options;
        _ = services.AddScoped(_ => new CoreDbContext(coreOptions));
        _ = services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>();
        _ = services.AddApplication();
        _ = services.AddSingleton(Options.Create(new AgentRuntimeSettings
        {
            Enabled = true,
            AllowedModels = [model],
            MaxRunPasses = 1,
        }));
        _ = services.AddSingleton(Options.Create(new EditorAgentSettings
        {
            Enabled = true,
            ResumeSessions = true,
            DefaultModelMatch = model,
        }));
        _ = services.AddSingleton(Options.Create(new AgentConversationSettings()));
        _ = services.AddSingleton<IAgentRunLimitsProvider, AgentRunLimitsCache>();
        _ = services.AddScoped<ITokenUsageService, TokenUsageService>();
        _ = services.AddSingleton<IFileStorageManager>(new FileStorageManager(
            new TestFileStorageResolver(new TestFileStorageService()),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
        var httpClient = new HttpClient(_starts) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _ = services.AddSingleton<IAgentRuntimeClient>(new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance));
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
            runInputStage: null!,
            runContentCheck: new PassRunContentCheck()));
        _ = services.AddSingleton<IJobsService>(provider => new JobsService(
            provider, NullLogger<JobsService>.Instance, Options.Create(new PlatformSettings())));
        _provider = services.BuildServiceProvider();

        Seed();
        _provider.GetRequiredService<IEditorAgentNotifications>().TurnUpdated += (_, update) => _updates.Enqueue(update);
    }

    public string Model { get; }

    public IWorkflowEditorAgentService Service => _provider.GetRequiredService<IWorkflowEditorAgentService>();

    /// <summary>
    /// The documents the runtime was sent for a turn, by file name, over every round of the stop
    /// gate: what the platform actually staged, not what the user picked.
    /// </summary>
    public IReadOnlyList<string> StagedDocumentNames(string conversationId, int turn)
    {
        return [.. Enumerable.Range(AgentRunIds.FIRST_PASS, EditorAgentProposalGate.MAX_ROUNDS)
            .Select(pass => AgentRunIds.ForConversationTurn(WorkflowEditorAgentService.PURPOSE, conversationId, turn, pass))
            .SelectMany(_starts.DocumentNames)
            .Distinct()];
    }

    /// <summary>
    /// The host for a live eval run, or a skip. The paid-API opt-in is the test attribute's; the
    /// evals also need their own opt-in, so a developer who exports the paid-API variables for
    /// another suite does not start 22 billed cases, and the runtime's address. A comma-separated
    /// case list in <c>INIA_TEST_EDITOR_AGENT_EVAL_CASES</c> runs only those cases.
    /// </summary>
    public static EditorAgentEvalHost RequireLive(string caseId)
    {
        if (Environment.GetEnvironmentVariable(EVALS_ENV) != OPTED_IN)
        {
            Assert.Skip($"Set {EVALS_ENV}={OPTED_IN} to run the editor agent evals.");
        }

        string? baseUrl = Environment.GetEnvironmentVariable(BASE_URL_ENV);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Skip($"Set {BASE_URL_ENV} to a running agent-runtime base URL to run the editor agent evals.");
        }

        string? cases = Environment.GetEnvironmentVariable(CASES_ENV);
        if (!string.IsNullOrWhiteSpace(cases) && !cases.Split(',', StringSplitOptions.TrimEntries).Contains(caseId))
        {
            Assert.Skip($"{caseId} is not in {CASES_ENV}.");
        }

        string? model = Environment.GetEnvironmentVariable(MODEL_ENV);
        return new EditorAgentEvalHost(baseUrl, string.IsNullOrWhiteSpace(model) ? DEFAULT_MODEL : model);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _provider.GetRequiredService<IJobsService>().StopEveryJobAsync(_stopJobsTimeout);
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

    public async Task<EditorAgentConversationView> StartAsync(CancellationToken cancellationToken)
    {
        ErrorOr<EditorAgentConversationView> started = await Service.GetOrStartConversationAsync(
            ORG_ID, USER_ID, EditorAgentEvalWorkflow.TYPE, cancellationToken);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Description : null);

        return started.Value;
    }

    /// <summary>Sends one message and waits for the turn to settle, then reads it back whole.</summary>
    public async Task<EditorAgentTurnView> AskAsync(
        string conversationId,
        string text,
        Workflow workflow,
        IReadOnlyList<string> reportIds,
        CancellationToken cancellationToken)
    {
        ErrorOr<EditorAgentTurnView> sent = await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversationId, text, Model, workflow) { ReportIds = reportIds },
            cancellationToken);
        Assert.False(sent.IsError, sent.IsError ? sent.FirstError.Description : null);
        int number = sent.Value.Number;

        await Eventually.UntilAsync(
            () => _updates.Any(update => update.ConversationId == conversationId
                && update.TurnNumber == number
                && update.Status is AgentConversationTurnStatus.Completed
                    or AgentConversationTurnStatus.Failed
                    or AgentConversationTurnStatus.Cancelled),
            "the editor agent's turn",
            TimeSpan.FromMinutes(TURN_TIMEOUT_MINUTES),
            cancellationToken: cancellationToken);

        ErrorOr<EditorAgentConversationView> conversation = await Service.GetConversationAsync(
            ORG_ID, USER_ID, conversationId, cancellationToken);
        return conversation.Value.Turns.Single(turn => turn.Number == number);
    }

    /// <summary>
    /// A past run of the eval workflow in the organization, whose given block answered with
    /// <paramref name="answer"/>: the report a leak case picks.
    /// </summary>
    public async Task<string> SeedReportAsync(string counterpartyName, string blockTag, string answer, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        var counterparty = new Counterparty(ORG_ID, counterpartyName);
        DateTime createdAt = DateTime.UtcNow.AddDays(-1);
        var execution = new WorkflowExecution
        {
            WorkflowTypeId = EditorAgentEvalWorkflow.TYPE,
            OrganizationId = ORG_ID,
            Title = $"{counterpartyName} report",
            WorkflowState = WorkflowState.Draft,
            Counterparty = counterparty,
            CounterpartyId = counterparty.Id,
            JobId = Guid.NewGuid().ToString("N"),
            CreatedAt = createdAt,
            CompletedAt = createdAt.AddMinutes(20),
        };
        execution.WorkflowId = execution.Id;
        _ = context.Counterparties.Add(counterparty);
        _ = context.WorkflowExecutions.Add(execution);
        _ = context.Workflows.Add(EditorAgentEvalWorkflow.Executed(execution.Id, ORG_ID, blockTag, answer));
        _ = await context.SaveChangesAsync(cancellationToken);

        return execution.Id;
    }

    private void Seed()
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.Organizations.Add(new Organization("Editor agent evals", ORG_ID));
        var editorRole = new IdentityRole(Roles.WORKFLOW_EDITOR) { NormalizedName = Roles.WORKFLOW_EDITOR.ToUpperInvariant() };
        _ = context.Roles.Add(editorRole);
        _ = context.Users.Add(new User
        {
            Id = USER_ID,
            OrganizationId = ORG_ID,
            UserName = USER_ID,
            FirstName = "Eval",
            LastName = "User",
        });
        _ = context.UserRoles.Add(new IdentityUserRole<string> { UserId = USER_ID, RoleId = editorRole.Id });
        _ = context.SaveChanges();

        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Add(new WorkflowType { Id = EditorAgentEvalWorkflow.TYPE, Name = TYPE_NAME });
        _ = core.Add(new WorkflowTypeOrganizationAccess { WorkflowTypeId = EditorAgentEvalWorkflow.TYPE, OrganizationId = ORG_ID });
        _ = core.SaveChanges();
    }
}

/// <summary>
/// Passes every request to the runtime unchanged and keeps, for each run start, the names of
/// the documents it carried.
/// </summary>
internal sealed class EditorAgentEvalStartRecorder(HttpMessageHandler runtime) : DelegatingHandler(runtime)
{
    private const string RUNS_PATH = "/v1/runs";

    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _documents = new();

    public IReadOnlyList<string> DocumentNames(string executionId)
    {
        return _documents.GetValueOrDefault(executionId, []);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && request.Content is not null
            && request.RequestUri?.AbsolutePath.EndsWith(RUNS_PATH, StringComparison.Ordinal) == true)
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            JsonElement root = body.RootElement;
            if (root.TryGetProperty("executionId", out JsonElement executionId) && root.TryGetProperty("documents", out JsonElement documents))
            {
                _documents[executionId.GetString()!] = [.. documents.EnumerateArray().Select(static document => document.GetProperty("name").GetString()!)];
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
