using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;

/// <summary>
/// Query to retrieve organization-wide token usage dashboard.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID (for authorization).</param>
public record GetOrganizationUsageDashboardQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<OrganizationUsageDashboardDto>>;
