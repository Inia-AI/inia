using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;

namespace IniaAi.Application.Commands.TwoFactor.EnableTotp;

/// <summary>
/// Command to enable TOTP-based 2FA after verifying the provided code.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="Code">The 6-digit verification code from the authenticator app.</param>
/// <param name="IpAddress">The IP address of the request.</param>
/// <param name="UserAgent">The User-Agent of the request.</param>
public record EnableTotpCommand(
    string OrganizationId,
    string UserId,
    string Code,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<EnableTotpResponse>>;
