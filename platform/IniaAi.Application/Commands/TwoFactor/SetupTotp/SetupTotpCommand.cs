using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;

namespace IniaAi.Application.Commands.TwoFactor.SetupTotp;

/// <summary>
/// Command to initiate TOTP setup by generating a new authenticator key.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="IpAddress">The IP address of the request.</param>
/// <param name="UserAgent">The User-Agent of the request.</param>
public record SetupTotpCommand(
    string OrganizationId,
    string UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<TotpSetupResponse>>;
