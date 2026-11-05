using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.TwoFactor.RemoveTrustedDevice;

/// <summary>
/// Command to remove a trusted device.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="DeviceId">The device ID to remove.</param>
/// <param name="IpAddress">The IP address of the request.</param>
/// <param name="UserAgent">The User-Agent of the request.</param>
public record RemoveTrustedDeviceCommand(
    string OrganizationId,
    string UserId,
    string DeviceId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<Deleted>>;
