namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// The producer side of an in-process hand-off between a command that commits a state change
/// and the worker that does the work off the request path. Requests are not durable: a
/// process restart loses the queue, the work's own state keeps saying it is due — an entity's
/// in-flight state, or a condition the user can see — and the remedy is the user-triggered
/// retry the design provides for; there is no background reaper.
/// </summary>
public interface IWorkQueue<in TRequest>
{
    ValueTask EnqueueAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The consumer side of the hand-off, held only by the worker that drains it.
/// </summary>
public interface IWorkQueueReader<TRequest>
{
    IAsyncEnumerable<TRequest> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Takes every request still waiting, for the worker's stop path: the host is going down
    /// and nothing will read them, so each is recorded as never started instead of silently
    /// lost — wherever that work's state lives.
    /// </summary>
    IReadOnlyList<TRequest> DrainPending();
}

/// <summary>
/// Runs one queued request end to end inside the worker's per-request scope.
/// </summary>
public interface IWorkRunner<in TRequest>
{
    Task RunAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the request was drained from the queue by a stopping host before it ran,
    /// so the work says why it did not proceed and offers its retry at once. Most runners
    /// write this on their entity; a runner whose undone work is itself user-visible state
    /// may only log it.
    /// </summary>
    Task RecordNotStartedAsync(TRequest request);
}
