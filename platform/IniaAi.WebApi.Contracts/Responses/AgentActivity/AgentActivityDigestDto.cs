using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.AgentActivity;

/// <summary>
/// The client-facing activity digest of one agent block run: ordered beats, the phase
/// vocabulary they reference, and receipt facts. This schema is deliberately unable to
/// represent workspace paths, run or session identifiers, raw commands, token counts, or
/// cost — nothing internal-shaped can reach a client through it, which
/// <c>AgentActivityDigestDtoContractTests</c> holds mechanically.
/// </summary>
public sealed record AgentActivityDigestDto
{
    /// <summary>
    /// Version of this digest shape, so a client can refuse a shape it does not know.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// How the run stands as of this digest.
    /// </summary>
    public required AgentActivityOutcome Outcome { get; init; }

    /// <summary>
    /// The agent run's own absolute start — the anchor every beat's <c>AtSeconds</c> is
    /// measured from. Null when the trace carries nothing timestamped. This is the run's
    /// start, not the workflow execution's.
    /// </summary>
    public required DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>
    /// The phase vocabulary beats point into, in display order.
    /// </summary>
    public required List<AgentActivityPhaseDto> Phases { get; init; }

    /// <summary>
    /// The paced feed lines, ordered by time. A re-projection only ever appends.
    /// </summary>
    public required List<AgentActivityBeatDto> Beats { get; init; }

    /// <summary>
    /// The receipt facts a settled feed collapses into.
    /// </summary>
    public required AgentActivityReceiptDto Receipt { get; init; }
}
