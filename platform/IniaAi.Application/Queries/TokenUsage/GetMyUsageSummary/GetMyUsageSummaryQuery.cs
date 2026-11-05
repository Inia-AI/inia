using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.GetMyUsageSummary;

/// <summary>
/// Query to retrieve personal token usage summary for the current user.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
public record GetMyUsageSummaryQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<MyUsageSummaryDto>>;
