using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;

/// <summary>
/// The client-facing activity digest of an executed Agent block, projected on demand from
/// its persisted trace pack. A block without a collected trace answers an empty digest,
/// never an error; a cross-org block is indistinguishable from one without a trace.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID for audit logging.</param>
/// <param name="BlockId">The executed block whose activity to project.</param>
/// <param name="BeatSource">Optional beat-source override: "narrated" or "classified"
/// (case-insensitive). Anything else resolves automatically — narrated when the run
/// narrated enough, classified otherwise. Exists so the same trace can be rendered under
/// either approach and judged side by side.</param>
/// <param name="Round">Optional round selector: the runs the block bought in that round of its
/// verification loop. Resolved through the block's own run ledger, the only record of which
/// round a run belongs to. A round the block bought nothing in answers an empty digest — never
/// another round's run, which would silently mislabel the leg on screen.</param>
/// <param name="RunExecutionId">Optional leg selector: exactly this run. Takes precedence over
/// <paramref name="Round"/>, which it already implies.</param>
public sealed record GetAgentRunActivityQuery(
    string OrganizationId,
    string UserId,
    string BlockId,
    string? BeatSource = null,
    int? Round = null,
    string? RunExecutionId = null) : IRequest<ErrorOr<AgentActivityDigestDto>>;
