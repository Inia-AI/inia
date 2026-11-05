using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.GetExecutionCost;

/// <summary>
/// What one workflow run cost, total and per block, read from its stored usage rows.
/// </summary>
/// <param name="OrganizationId">The caller's organization ID for tenant isolation.</param>
/// <param name="UserId">The caller's user ID.</param>
/// <param name="ExecutionId">The workflow execution.</param>
public sealed record GetExecutionCostQuery(
    string OrganizationId,
    string UserId,
    string ExecutionId) : IRequest<ErrorOr<ExecutionCostDto>>
{
    /// <summary>True when the caller is a super admin. Otherwise only the service user that started the run may read it.</summary>
    public bool CallerIsSuperAdmin { get; init; }
}
