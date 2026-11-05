using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;

namespace IniaAi.Application.Queries.TwoFactor.GetTwoFactorStatus;

/// <summary>
/// Query to get the current 2FA status for a user.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
public record GetTwoFactorStatusQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<TwoFactorStatusResponse>>;
