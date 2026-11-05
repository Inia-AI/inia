namespace IniaAi.Core.Workflows.Executions;

/// <summary>
/// This process start's <c>HostBoot</c> id, fixed for the life of the process. Every execution row the
/// process creates records it, because the in-memory job queue that runs the row lives here. It also
/// keeps when the process's own heartbeat began succeeding without a break.
/// </summary>
public sealed class HostBootIdentity
{
    private long _heartbeatHealthySinceTicks;

    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>Null until the first heartbeat succeeds, and again after any heartbeat fails.</summary>
    public DateTimeOffset? HeartbeatHealthySince
    {
        get
        {
            long ticks = Interlocked.Read(ref _heartbeatHealthySinceTicks);

            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void HeartbeatSucceeded(DateTimeOffset at)
    {
        _ = Interlocked.CompareExchange(ref _heartbeatHealthySinceTicks, at.UtcTicks, 0);
    }

    public void HeartbeatFailed()
    {
        _ = Interlocked.Exchange(ref _heartbeatHealthySinceTicks, 0);
    }
}
