using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

/// <summary>
/// The bytes of one collected file of an Agent block's run inside a workflow execution. Without
/// <see cref="RunExecutionId"/> the block's most recently collected run answers, and without
/// <see cref="Attempt"/> that run's latest collection attempt.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID.</param>
/// <param name="ExecutionId">The workflow execution the block ran in.</param>
/// <param name="BlockId">The executed block.</param>
/// <param name="Path">The file's path inside the run pack.</param>
/// <param name="RunExecutionId">Optional: one run of the block.</param>
/// <param name="Attempt">Optional: one collection attempt of that run.</param>
public sealed record GetExecutionAgentRunFileQuery(
    string OrganizationId,
    string UserId,
    string ExecutionId,
    string BlockId,
    string Path,
    string? RunExecutionId,
    int? Attempt) : IRequest<ErrorOr<AgentRunFileContent>>
{
    /// <summary>True when the caller's role admits every run in the organization.</summary>
    public bool CallerReadsEveryRun { get; init; }
}
