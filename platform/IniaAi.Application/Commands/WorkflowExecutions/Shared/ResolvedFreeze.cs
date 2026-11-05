using IniaAi.Domain.Replay;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// What the engine needs from a freeze source: the captures to seed, the provenance to stamp,
/// and what an uncovered freeze-eligible block does. Read once, so the source is dispatched in
/// one place.
/// </summary>
public sealed record ResolvedFreeze(
    IReadOnlyList<ReplayOutput>? Outputs,
    string? ExecutionId,
    string? CaseId,
    ReplayUncoveredBlockPolicy UncoveredPolicy);
