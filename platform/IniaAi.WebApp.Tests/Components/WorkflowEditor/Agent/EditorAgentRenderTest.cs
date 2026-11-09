using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Services.Jobs;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

/// <summary>
/// Renders the editor agent's components over the real agent service: its jobs, the shared
/// conversation runner and executor, and the SQLite user-data database. Only the agent runtime's
/// HTTP client is a stand-in, the boundary the platform calls out through.
/// </summary>
public abstract class EditorAgentRenderTest : SignedInRenderTest
{
    protected const string SONNET = "us.anthropic.claude-sonnet-5";
    protected const string HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"editor-agent-panel-{Guid.NewGuid():N}.db");

    protected EditorAgentRenderTest(bool enabled)
    {
        // Turns run on job threads beside the render's own reads, and the base class's one shared
        // in-memory connection is not safe across threads: a file database with a connection per context.
        string userDataConnectionString = $"DataSource={_databasePath};Pooling=False";
        SqliteUserDataSchema.CreateFileDatabase(userDataConnectionString);
        DbContextOptions<UserDataDbContext> userDataOptions = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(userDataConnectionString).Options;
        _ = Services.AddSingleton(userDataOptions);
        _ = Services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(userDataOptions));
        _ = Services.AddSingleton<IDbContextFactory<UserDataDbContext>>(new SqliteUserDataDbContextFactory(userDataOptions));
        _ = Services.AddMudMarkdownServices();
        _ = Services.AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>());
        DbContextOptions<CoreDbContext> coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"editor-agent-core-{Guid.NewGuid()}").Options;
        _ = Services.AddScoped(_ => new CoreDbContext(coreOptions));
        _ = Services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        _ = Services.AddSingleton(Options.Create(new AgentRuntimeSettings
        {
            Enabled = true,
            AllowedModels = [HAIKU, SONNET],
            MaxRunPasses = 1,
        }));
        _ = Services.AddSingleton(Options.Create(new EditorAgentSettings { Enabled = enabled }));
        _ = Services.AddSingleton(Options.Create(new AgentConversationSettings()));
        _ = Services.AddSingleton<IAgentRunLimitsProvider, AgentRunLimitsCache>();
        _ = Services.AddScoped<ITokenUsageService, TokenUsageService>();
        _ = Services.AddSingleton<IFileStorageManager>(new FileStorageManager(
            new TestFileStorageResolver(new TestFileStorageService()),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
        _ = Services.AddSingleton<IAgentRuntimeClient>(Runtime);
        _ = Services.AddSingleton<IDbExceptionAnalyzer, DbExceptionAnalyzer>();
        _ = Services.AddSingleton<IAgentRunActivityFeed, AgentRunActivityFeed>();
        _ = Services.AddSingleton<AgentRunPollGate>();
        _ = Services.AddTransient<IAgentRunTraceCollector, AgentRunTraceCollector>();
        _ = Services.AddTransient<IAgentRunTracePersister, AgentRunTracePersister>();
        _ = Services.AddTransient<IAgentRunSessionBundleCustodian, AgentRunSessionBundleCustodian>();
        _ = Services.AddTransient<IAgentRunSessionBundleReaper, AgentRunSessionBundleReaper>();
        _ = Services.AddTransient<IAgentRunExecutor>(provider => new AgentRunExecutor(
            provider.GetRequiredService<IAgentRuntimeClient>(),
            provider.GetRequiredService<AgentRunPollGate>(),
            NullLogger<AgentRunExecutor>.Instance,
            provider.GetRequiredService<IAgentRunTraceCollector>(),
            provider.GetRequiredService<IAgentRunTracePersister>(),
            timeProvider: null,
            activityFeed: provider.GetRequiredService<IAgentRunActivityFeed>(),
            narrationTidier: null,
            bundleCustodian: provider.GetRequiredService<IAgentRunSessionBundleCustodian>()));
        _ = Services.AddAgentConversations();
        _ = Services.AddTransient<IAgentConversationRunner, AgentConversationRunner>();
        _ = Services.AddTransient<IJobExecutor>(provider => new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: null!,
            emailIntakeJobStage: null!,
            runInputStage: null!,
            runContentCheck: new PassRunContentCheck()));
        _ = Services.AddSingleton<IJobsService>(provider => new JobsService(
            provider, NullLogger<JobsService>.Instance, Options.Create(new PlatformSettings())));
    }

    protected StubAgentRuntimeClient Runtime { get; } = new();

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await Services.GetRequiredService<IJobsService>().StopEveryJobAsync();
        }
        finally
        {
            try
            {
                await base.DisposeAsyncCore();
            }
            finally
            {
                SqliteTestDatabaseFile.Delete(_databasePath);
            }
        }
    }

    /// <summary>
    /// The workflow type a conversation is about, open to <see cref="SignedInRenderTest.ORGANIZATION_ID"/>:
    /// the service refuses a type that does not exist or that the organization cannot use.
    /// </summary>
    protected async Task SeedWorkflowTypeAsync(string workflowTypeId, string? name = null)
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Add(new WorkflowType { Id = workflowTypeId, Name = name ?? workflowTypeId });
        _ = core.Add(new WorkflowTypeOrganizationAccess { WorkflowTypeId = workflowTypeId, OrganizationId = ORGANIZATION_ID });
        _ = await core.SaveChangesAsync();
    }
}
