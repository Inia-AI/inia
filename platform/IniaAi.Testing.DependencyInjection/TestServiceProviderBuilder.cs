using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Application;
using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Queues;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Core.AugmentedSearch;
using IniaAi.Core.AugmentedSearch.Exa;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Rag;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Pdf;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Services.Documents;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.Testing.Documents;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace IniaAi.Testing.DependencyInjection;

public class TestServiceProviderBuilder
{
    private readonly IServiceCollection _services;
    private readonly PlatformSettings _platformSettings;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _databaseName;

    /// <summary>
    /// Builds the provider the open build has: the open defaults of every seam and no closed
    /// module, so a test can show what a deployment without the paid modules does.
    /// </summary>
    public bool WithoutClosedModules { get; init; }

    /// <summary>
    /// Every role a host that installs every closed module defines: the platform's own and each
    /// module's. A build without a module defines none of its roles, so its tests sweep fewer.
    /// </summary>
    public static IReadOnlyList<string> EveryDefinedRole { get; } = DefinedRolesWithEveryClosedModule();

    public TestServiceProviderBuilder(
        PlatformSettings platformSettings,
        ITestOutputHelper testOutputHelper,
        string? databaseName = null)
    {
        _services = new ServiceCollection();
        _platformSettings = platformSettings;
        _testOutputHelper = testOutputHelper;
        _databaseName = databaseName ?? $"IniaAi-TestDatabase-{Guid.NewGuid()}";
    }

    public IServiceProvider Build(Action<IServiceCollection>? overrideServices = null)
    {
        ConfigureLogging();
        ConfigurePlatformSettings();
        ConfigureDatabaseServices();
        ConfigureTransientServices();
        ConfigureScopedServices();
        ConfigureSingletonServices();
        ConfigureKeyedServices();
        ConfigureClosedModuleServices();

        overrideServices?.Invoke(_services);

        Microsoft.Extensions.DependencyInjection.ServiceProvider serviceProvider = _services.BuildServiceProvider();

        TestDatabaseServices.SeedTestData(serviceProvider);

        return serviceProvider;
    }

    private void ConfigureLogging()
    {
        _ = _services.AddSingleton<ILoggerProvider>(_ => new TestsLoggerProvider(_testOutputHelper));
        _ = _services.AddSingleton<ILoggerFactory>(provider =>
        {
            var loggerFactory = new LoggerFactory();
            foreach (ILoggerProvider loggerProvider in provider.GetServices<ILoggerProvider>())
            {
                loggerFactory.AddProvider(loggerProvider);
            }

            return loggerFactory;
        });
        _ = _services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    }

    private void ConfigurePlatformSettings()
    {
        _ = _services.AddSingleton(Options.Create(_platformSettings));
        _ = _services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // Configure file storage settings for tests
        var fileStorageSettings = new FileStorageSettings
        {
            DefaultProvider = "Database"
        };
        _ = _services.AddSingleton(Options.Create(fileStorageSettings));
    }

    private void ConfigureDatabaseServices()
    {
        _ = _services.AddTestDatabaseServices(_databaseName);
        _ = _services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = _services.AddStoredKeys();
    }

    private void ConfigureTransientServices()
    {
        _ = _services.AddTransient<TenantModelFamilyResolver>()
            .AddTransient<PdfExtractor>()
            .AddFileFormatsUnderTest()
            .AddTransient<DocumentAnalysisService>()
            .AddTransient<ReconciliationService>()
            .AddTransient<RagService>()
            .AddTransient<IExaSearchService, ExaSearchService>()
            .AddTransient<IWorkflowOutputBuilder, WorkflowOutputBuilder>()
            .AddTransient<IWorkflowOutputValidator, WorkflowOutputValidator>()
            .AddTransient<IWorkflowOutputService, WorkflowOutputService>()
            .AddTransient<OcrTranscriptionService>()
            .AddTransient<BlockExecutionContext>()
            .AddTransient<IFileStorageManager, FileStorageManager>()
            .AddWorkQueue<DocumentProcessingQueue, DocumentProcessingRequest, DocumentProcessingRunner>()
            .AddTransient<IDocumentProcessingService, DocumentProcessingService>()
            .AddTransient<IEntityPlaceholderResolver, EntityPlaceholderResolver>()
            .AddTransient<ExcelSheetAnalysisService>();
    }

    private static void ConfigureScopedServices()
    {
    }

    private void ConfigureSingletonServices()
    {
        _ = _services.AddSingleton(_ => GetChatConnector())
            .AddSingleton(_ => GetEmbeddingsConnector())
            .AddSingleton<TestFileStorageService>()
            .AddSingleton<IFileStorageResolver, TestFileStorageResolver>()
            .AddSingleton<IAgentRuntimeClient, StubAgentRuntimeClient>()
            .AddSingleton<IAgentRunTraceCollector, AgentRunTraceCollector>()
            .AddSingleton<IAgentRunTracePersister, StubAgentRunTracePersister>()
            .AddSingleton<AgentRunPollGate>()
            .AddSingleton<IAgentRunExecutor, AgentRunExecutor>()
            .AddSingleton<TestTokenUsageService>()
            .AddSingleton<ITokenUsageService>(sp => sp.GetRequiredService<TestTokenUsageService>())
            .AddMemoryCache();

        _ = _services.AddSingleton(sp =>
        {
            PlatformSettings config = sp.GetRequiredService<IOptions<PlatformSettings>>().Value;
            int tokenLimit = config.Anthropic.MaxInputTokensPerWindow;
            ILogger<TokenSlidingWindowRateLimiter> logger = sp.GetRequiredService<ILogger<TokenSlidingWindowRateLimiter>>();
            return new TokenSlidingWindowRateLimiter(tokenLimit, TimeSpan.FromMinutes(1), logger);
        });

        _ = _services.AddHttpClient<IExchangeRateService, ExchangeRateService>()
            .ConfigurePrimaryHttpMessageHandler(() => new CannedExchangeRatesHandler());
    }

    private void ConfigureKeyedServices()
    {
        _ = _services.AddWorkflowScheduling();
        _ = _services.AddKeyedTransient<IWorkflowHandler, WorkflowHandler>(WorkflowType.GENERIC);
        _ = _services.AddTransient<WorkflowHandler>();
    }

    private void ConfigureClosedModuleServices()
    {
        _ = _services.AddSeamDefaults();

        if (WithoutClosedModules)
        {
            return;
        }

        AddClosedModules(_services);
    }

    private static void AddClosedModules(IServiceCollection services)
    {
    }

    private static IReadOnlyList<string> DefinedRolesWithEveryClosedModule()
    {
        var services = new ServiceCollection();
        AddClosedModules(services);
        _ = services.AddSeamDefaults();
        using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<RoleGrants>().DefinedRoles;
    }

    private IChatConnector GetChatConnector()
    {
        return TestChatConnectorFactory.Create(
            _platformSettings,
            TestsLogger.CreateLogger<IChatConnector>(_testOutputHelper));
    }

    private IEmbeddingsConnector GetEmbeddingsConnector()
    {
        return TestEmbeddingsConnectorFactory.Create(
            _platformSettings,
            TestsLogger.CreateLogger<IEmbeddingsConnector>(_testOutputHelper));
    }
}
