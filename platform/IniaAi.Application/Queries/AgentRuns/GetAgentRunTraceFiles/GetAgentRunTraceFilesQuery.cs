using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunTraceFiles;

/// <summary>
/// The persisted trace evidence of an executed Agent block, for on-demand display — trace rows
/// are deliberately never eager-loaded with the workflow graph because each carries the raw
/// file bytes.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID for audit logging.</param>
/// <param name="BlockId">The executed block whose trace rows to read.</param>
public sealed record GetAgentRunTraceFilesQuery(
    string OrganizationId,
    string UserId,
    string BlockId) : IRequest<ErrorOr<List<AgentRunTraceFile>>>;
