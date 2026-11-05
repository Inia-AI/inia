using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IniaAi.Services.Queues;

/// <summary>
/// The registration a real host uses: the queue with both of its faces, the runner, and the
/// hosted worker that drains it, in one call — an in-memory queue with no drain strands its
/// entities mid-state, so the two must not be registrable apart in a host. The three-type
/// form in <c>WorkQueueRegistration</c> exists for the test composition root, which is
/// deliberately worker-less.
/// </summary>
public static class WorkQueueHosting
{
    public static IServiceCollection AddWorkQueue<TQueue, TRequest, TRunner, TWorker>(this IServiceCollection services)
        where TQueue : class, IWorkQueue<TRequest>, IWorkQueueReader<TRequest>
        where TRunner : class, IWorkRunner<TRequest>
        where TWorker : BackgroundService
    {
        return services
            .AddWorkQueue<TQueue, TRequest, TRunner>()
            .AddHostedService<TWorker>();
    }
}
