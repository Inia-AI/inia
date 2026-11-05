using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.TwoFactor.RemoveAllTrustedDevices;

/// <summary>
/// Command to remove all trusted devices for a user.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="IpAddress">The IP address of the request.</param>
/// <param name="UserAgent">The User-Agent of the request.</param>
public record RemoveAllTrustedDevicesCommand(
    string OrganizationId,
    string UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<Success>>;
