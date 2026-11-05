using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.GetWorkflowTypeUsageSummary;

/// <summary>
/// Query to retrieve token usage summary for a specific workflow type.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="WorkflowTypeId">The workflow type ID.</param>
public record GetWorkflowTypeUsageSummaryQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<WorkflowTypeUsageSummaryDto>>;
