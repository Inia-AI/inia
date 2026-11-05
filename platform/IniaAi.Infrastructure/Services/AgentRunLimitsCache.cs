using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The one process-wide holder of the runtime's fetched fuse limits, written by
/// <see cref="AgentRunLimitsRefreshService"/> and read lock-free by save-time validation and the
/// editor. Freshness is the enforcement gate: a snapshot older than <see cref="FreshnessWindow"/>
/// stops being <see cref="Enforceable"/> (ceilings move with runtime deployments, and a runtime
/// that has been unreachable that long may be mid-redeploy), while <see cref="LastKnown"/> keeps
/// it available for display. The window deliberately spans many refresh intervals, so one failed
/// fetch never flips saves into the warn-instead-of-check path.
/// </summary>
public class AgentRunLimitsCache : IAgentRunLimitsProvider
{
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _timeProvider;
    private AgentRunLimitsSnapshot? _snapshot;

    public AgentRunLimitsCache(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AgentRunLimitsSnapshot? LastKnown => Volatile.Read(ref _snapshot);

    public AgentRunLimits? Enforceable
    {
        get
        {
            AgentRunLimitsSnapshot? snapshot = Volatile.Read(ref _snapshot);

            return snapshot is not null && _timeProvider.GetUtcNow() - snapshot.FetchedAt <= FreshnessWindow
                ? snapshot.Limits
                : null;
        }
    }

    public void Store(AgentRunLimits limits)
    {
        Volatile.Write(ref _snapshot, new AgentRunLimitsSnapshot(limits, _timeProvider.GetUtcNow()));
    }
}
