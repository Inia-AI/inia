using IniaAi.AiConnection.Abstractions;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Infrastructure.Persistence.Graph;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IniaAi.Common.Interfaces.Services;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class GraphQueryServiceTestBuilder
{
    public const string MODEL_ANSWER = "The target block reads the input block.";

    private readonly IServiceCollection _services;
    private readonly PlatformSettings _platformSettings;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IChatConnector _chatConnector;

    /// <param name="chatConnector">The scripted model; null answers every call with <see cref="MODEL_ANSWER"/>.</param>
    public GraphQueryServiceTestBuilder(
        PlatformSettings platformSettings,
        ITestOutputHelper testOutputHelper,
        IChatConnector? chatConnector = null)
    {
        _services = new ServiceCollection();
        _platformSettings = platformSettings;
        _testOutputHelper = testOutputHelper;
        _chatConnector = chatConnector ?? ScriptedChatConnector.Answering(
            $$"""<answer>{"action": "answer", "answer": "{{MODEL_ANSWER}}"}</answer>""");
    }

    public IServiceProvider Build()
    {
        ConfigureLogging();
        ConfigurePlatformSettings();
        ConfigureDatabaseServices();
        ConfigureGraphServices();
        ConfigureAiServices();

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
            loggerFactory.AddProvider(provider.GetRequiredService<ILoggerProvider>());
            return loggerFactory;
        });
        _ = _services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    }

    private void ConfigurePlatformSettings()
    {
        _ = _services.AddSingleton(Options.Create(_platformSettings));

        var fileStorageSettings = new FileStorageSettings
        {
            DefaultProvider = "Database"
        };
        _ = _services.AddSingleton(Options.Create(fileStorageSettings));
    }

    private void ConfigureDatabaseServices()
    {
        _ = _services.AddTestDatabaseServices();
    }

    private void ConfigureGraphServices()
    {
        _ = _services.AddScoped<IGraphRepository, GraphRepository>();
        _ = _services.AddScoped<IWorkflowGraphBuilder, WorkflowGraphBuilder>();
        _ = _services.AddScoped<IGraphBuilderService, GraphBuilderService>();
        _ = _services.AddScoped<IRequestModelUse, RequestModelUse>();
        _ = _services.AddScoped<IGraphQueryService, GraphQueryService>();
    }

    private void ConfigureAiServices()
    {
        _ = _services.AddSingleton(_chatConnector);
    }
}
