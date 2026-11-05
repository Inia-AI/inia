namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Process-wide cap on concurrent agent-run polls: the gate protects the one shared sidecar
/// from a poll stampede across every concurrent workflow, so its scope is the process — a DI
/// singleton rather than a static, which makes the executor's poller unconstructable without
/// it. Held per poll, never across a run's lifetime. The cap stays a constant here until
/// operations needs per-host stampede tuning.
/// </summary>
public sealed class AgentRunPollGate
{
    public const int MAX_CONCURRENT_POLLS = 8;

    private readonly SemaphoreSlim _gate = new(MAX_CONCURRENT_POLLS, MAX_CONCURRENT_POLLS);

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _gate.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        _ = _gate.Release();
    }
}
