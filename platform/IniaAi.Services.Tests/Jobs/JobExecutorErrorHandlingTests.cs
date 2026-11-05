using IniaAi.Core.Documents;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// A job that fails is handled once, against a real database: the failure is logged, the job's
/// error is set and the execution is marked once, so nothing the handler writes lands twice.
/// </summary>
public sealed class JobExecutorErrorHandlingTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-error-handling";
    private const string WORKFLOW_TYPE_ID = "TypeWithNoHandler";
    private const string ERROR_LOG_PREFIX = "Error processing job";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly CapturingLogger _logger = new();

    public JobExecutorErrorHandlingTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AJobThatFails_IsHandledOnce()
    {
        var job = new Job(ORGANIZATION_ID, WORKFLOW_TYPE_ID) { Workflow = new Workflow { WorkflowTypeId = WORKFLOW_TYPE_ID } };
        var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);

        CreateExecutor().Execute(job, finished.SetResult, TestContext.Current.CancellationToken);
        _ = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        LogEntry failure = Assert.Single(_logger.Entries, entry => entry.Message.StartsWith(ERROR_LOG_PREFIX, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.NotNull(job.Error);
    }

    private JobExecutor CreateExecutor()
    {
        IServiceScopeFactory scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        return new JobExecutor(
            _logger,
            scopeFactory,
            documentProcessing: null!,
            workflowOutputService: new WorkflowOutputService(
                outputBuilder: null!,
                workflowOutputValidator: null!,
                scopeFactory,
                NullLogger<WorkflowOutputService>.Instance),
            emailIntakeJobStage: null!,
            runInputStage: new PassThroughRunInputStage());
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<JobExecutor>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
