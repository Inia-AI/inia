using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;

namespace IniaAi.Application.Queries.TwoFactor.GetTrustedDevices;

/// <summary>
/// Query to get the list of trusted devices for a user.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
public record GetTrustedDevicesQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<List<TrustedDeviceResponse>>>;
