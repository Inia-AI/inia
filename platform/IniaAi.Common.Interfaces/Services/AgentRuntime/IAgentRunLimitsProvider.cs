namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// A successful limits fetch and when it happened. Age is part of the contract: ceilings move
/// with runtime deployments, so a consumer deciding whether to <i>enforce</i> must go through
/// <see cref="IAgentRunLimitsProvider.Enforceable"/> rather than reading a snapshot of unknown
/// staleness as truth.
/// </summary>
public sealed record AgentRunLimitsSnapshot(AgentRunLimits Limits, DateTimeOffset FetchedAt);

/// <summary>
/// The platform's cached view of the runtime's deployed fuse limits (<c>GET /v1/limits</c>),
/// refreshed in the background. Both members answer without I/O, so save-time validation and the
/// editor never wait on the sidecar.
/// <para>
/// The two properties are two deliberate trust levels. <see cref="Enforceable"/> is non-null only
/// while the cache is fresh — it is what save-time validation may refuse an author over. A stale
/// or never-filled cache answers null, and callers must then <b>warn instead of refuse</b>: a
/// runtime outage at save must never stop an author saving work the runtime itself would accept,
/// and the runtime's 400 at run start still refuses an over-ceiling fuse before any spend.
/// <see cref="LastKnown"/> keeps the last successful fetch regardless of age, for display
/// (editor placeholders and helper text) — never for enforcement.
/// </para>
/// </summary>
public interface IAgentRunLimitsProvider
{
    AgentRunLimitsSnapshot? LastKnown { get; }

    AgentRunLimits? Enforceable { get; }
}
