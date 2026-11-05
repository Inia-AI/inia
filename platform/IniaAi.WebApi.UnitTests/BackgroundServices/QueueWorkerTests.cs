using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Queues;
using IniaAi.WebApi.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.WebApi.UnitTests.BackgroundServices;

public class QueueWorkerTests
{
    [Fact]
    public async Task Stop_DrainsTheRequestsThatNeverStarted_AndRecordsThemAsNotStarted()
    {
        RecordingRunner runner = new();
        FakeQueue queue = new();
        ServiceCollection services = new();
        _ = services.AddSingleton<IWorkRunner<string>>(runner);
        await using ServiceProvider provider = services.BuildServiceProvider();
        SingleLaneWorker worker = new(queue, provider.GetRequiredService<IServiceScopeFactory>());

        await queue.EnqueueAsync("first", TestContext.Current.CancellationToken);
        await queue.EnqueueAsync("second", TestContext.Current.CancellationToken);
        await queue.EnqueueAsync("third", TestContext.Current.CancellationToken);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await runner.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first"], runner.Started);
        Assert.Equal(["first"], runner.Interrupted);
        Assert.Equal(["second", "third"], runner.NotStarted);
    }

    private sealed class FakeQueue : WorkQueue<string>;

    private sealed class SingleLaneWorker : QueueWorker<string>
    {
        public SingleLaneWorker(IWorkQueueReader<string> queue, IServiceScopeFactory scopeFactory)
            : base(NullLogger<SingleLaneWorker>.Instance, queue, scopeFactory)
        {
        }

        protected override int MaxConcurrency => 1;

        protected override string Describe(string request)
        {
            return request;
        }
    }

    private sealed class RecordingRunner : IWorkRunner<string>
    {
        public List<string> Started { get; } = [];

        public List<string> Interrupted { get; } = [];

        public List<string> NotStarted { get; } = [];

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(string request, CancellationToken cancellationToken)
        {
            Started.Add(request);
            FirstStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interrupted.Add(request);
                throw;
            }
        }

        public Task RecordNotStartedAsync(string request)
        {
            NotStarted.Add(request);
            return Task.CompletedTask;
        }
    }
}
