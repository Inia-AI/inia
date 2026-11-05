using IniaAi.Application.Common.Interfaces.Services;
using System.Threading.Channels;

namespace IniaAi.Services.Queues;

/// <summary>
/// An unbounded in-process channel: a command enqueues after its own commit, the worker
/// reads. Nothing here survives a restart — by design; the entity's in-flight state and the
/// user-triggered retry are the recovery path.
/// </summary>
public abstract class WorkQueue<TRequest> : IWorkQueue<TRequest>, IWorkQueueReader<TRequest>
{
    private readonly Channel<TRequest> _channel = Channel.CreateUnbounded<TRequest>();

    public ValueTask EnqueueAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<TRequest> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public IReadOnlyList<TRequest> DrainPending()
    {
        List<TRequest> pending = [];

        while (_channel.Reader.TryRead(out TRequest? request))
        {
            pending.Add(request);
        }

        return pending;
    }
}
