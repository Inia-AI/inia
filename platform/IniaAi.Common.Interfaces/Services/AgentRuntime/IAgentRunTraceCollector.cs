using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Collects a settled run's trace pack and published <c>out/</c> artifacts off the runtime
/// into <see cref="AgentRunTraceFile"/> rows (the path prefix — <c>trace/</c> vs <c>out/</c> —
/// says which a row is).
/// Part of the shared agent-integration layer and block-agnostic by design: rows come back
/// unanchored — null <see cref="AgentRunTraceFile.BlockId"/>, empty
/// <see cref="AgentRunTraceFile.OrganizationId"/> — and the caller stamps its own identity (the
/// Agent block stamps its block id and organization; every consumer stamps at least its
/// organization). Never throws — every failure, including cancellation, lands in the result's
/// flag and diagnostics, because evidence collection must never take down the run outcome it
/// evidences.
/// </summary>
public interface IAgentRunTraceCollector
{
    Task<AgentRunTraceCollectionResult> CollectAsync(
        string executionId,
        int attempt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of one collection attempt. <see cref="Files"/> carries only rows whose bytes were
/// fetched and verified against the manifest sha256 — on partial failure the verified subset is
/// still returned so callers keep what evidence exists. <see cref="Succeeded"/> false means the
/// pack is incomplete beyond the declared size cap (list failure, fetch failure, or integrity
/// mismatch): callers must then withhold the run's <c>/complete</c> acknowledgement so the pack
/// stays collectable on the runtime. Cap skips are a deliberate bound, not a failure — but any
/// published <c>out/</c> row the pack does not hold, whether capped, unfetchable,
/// digest-mismatched, or hidden behind a lying manifest kind, is a deliverable the platform now
/// holds no copy of, so those paths ride <see cref="DroppedArtifactPaths"/> as a typed signal:
/// the collected rows are the platform's only copy of a member's <c>out/</c>, and a
/// continuation re-carrying from a short set must know it is short. A failed listing
/// leaves the set unknowable — <see cref="Succeeded"/> false is the signal then, not this list.
/// Dropped trace files stay diagnostics only.
/// </summary>
public sealed record AgentRunTraceCollectionResult(
    bool Succeeded,
    IReadOnlyList<AgentRunTraceFile> Files,
    IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyList<string> DroppedArtifactPaths { get; init; } = [];
}
