using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

/// <summary>
/// Every collected agent run file of one workflow execution, without the bytes.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID.</param>
/// <param name="ExecutionId">The workflow execution whose agent runs to list.</param>
public sealed record ListExecutionAgentRunFilesQuery(
    string OrganizationId,
    string UserId,
    string ExecutionId) : IRequest<ErrorOr<List<AgentRunFileDto>>>
{
    /// <summary>True when the caller's role admits every run in the organization.</summary>
    public bool CallerReadsEveryRun { get; init; }
}
