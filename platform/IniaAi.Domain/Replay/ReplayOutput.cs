namespace IniaAi.Domain.Replay;

/// <summary>
/// A captured external-block output to replay into a regression rerun, identified by the
/// block's stable id (the section/tag/type fields are diagnostic context for unmatched
/// reporting). Freezing these means only the workflow logic is re-run, not live external
/// lookups (web search, FX rate, external API).
/// </summary>
public sealed record ReplayOutput(
    string SectionName,
    string ReplacementTag,
    string BlockType,
    string? RawResponse,
    string? JsonResponse,
    string? StableBlockId = null);

/// <summary>
/// A replay output (or freeze-eligible block) that could not be safely mapped during a rerun,
/// which makes the run inconclusive. <see cref="Reason"/> explains which policy fired:
/// not found, ambiguous, or an uncovered freeze-eligible block with no captured output.
/// </summary>
public sealed record ReplayUnmatched(
    string SectionName,
    string ReplacementTag,
    string BlockType,
    string Reason);
