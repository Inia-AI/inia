using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Commits collected trace rows to the platform's durable store before the run they evidence
/// is acknowledged. Part of the shared agent-integration layer: the executor calls it between
/// collection and the complete call, so the runtime's copy of a pack is released only once the
/// platform holds its own (an acknowledge over bytes still in memory turns any later
/// writer failure into permanent evidence loss). Rows arrive already anchored — the executor
/// stamps <see cref="AgentRunExecutionOptions.TraceAnchor"/> onto them. Never throws — a
/// persistence failure is reported as <c>false</c> so the caller withholds the acknowledgement
/// and the pack stays collectable on the runtime for its retention window. Replay-idempotent by
/// the row's natural key (run execution id, attempt, path): re-persisting after a crash or a
/// replayed teardown inserts nothing twice. Concurrent persists of the same run may duplicate
/// rows — see the implementation for why that race is accepted.
/// </summary>
public interface IAgentRunTracePersister
{
    /// <summary>
    /// True only when every row is durably committed (rows already present under their natural
    /// key count as committed). False on any failure — the caller must then withhold the run's
    /// acknowledgement.
    /// </summary>
    Task<bool> PersistAsync(
        IReadOnlyList<AgentRunTraceFile> files,
        CancellationToken cancellationToken = default);
}
