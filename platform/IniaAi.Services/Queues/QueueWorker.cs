using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Queues;

/// <summary>
/// Drains one work queue, a few requests at a time, each in its own scope, so the
/// work runs off the request path with the same DI graph a request would get and one long
/// item does not hold every other behind it. A failing request is logged and the next one
/// runs; the work it belonged to carries the error and offers a retry. When the host stops,
/// the requests still waiting are drained and each is recorded as never started — the queue is
/// not durable, so the work must say why it did not proceed rather than wait out a deadline.
/// </summary>
public abstract class QueueWorker<TRequest> : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IWorkQueueReader<TRequest> _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueWorker{TRequest}"/> class.
    /// </summary>
    protected QueueWorker(
        ILogger logger,
        IWorkQueueReader<TRequest> queue,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// How many requests run at once; each holds a model pipeline, so this stays small.
    /// </summary>
    protected abstract int MaxConcurrency { get; }

    /// <summary>
    /// Names a request for the log line written when it fails outside its runner.
    /// </summary>
    protected abstract string Describe(TRequest request);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                _queue.ReadAllAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = stoppingToken },
                RunOneAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await RecordDrainedAsync();
        }
    }

    private async Task RecordDrainedAsync()
    {
        IReadOnlyList<TRequest> drained = _queue.DrainPending();

        if (drained.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Shutdown drained {Count} queued request(s); recording each as not started", drained.Count);

        foreach (TRequest request in drained)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IWorkRunner<TRequest> runner = scope.ServiceProvider.GetRequiredService<IWorkRunner<TRequest>>();
                await runner.RecordNotStartedAsync(request);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "{Request} could not be recorded as not started; it stays queued on the entity until its retry", Describe(request));
            }
        }
    }

    private async ValueTask RunOneAsync(TRequest request, CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IWorkRunner<TRequest> runner = scope.ServiceProvider.GetRequiredService<IWorkRunner<TRequest>>();
            await runner.RunAsync(request, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{Request} failed outside the runner", Describe(request));
        }
    }
}
