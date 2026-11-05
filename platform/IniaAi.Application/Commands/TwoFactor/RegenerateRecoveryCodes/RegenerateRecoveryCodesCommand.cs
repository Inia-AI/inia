using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;

namespace IniaAi.Application.Commands.TwoFactor.RegenerateRecoveryCodes;

/// <summary>
/// Command to generate new recovery codes, invalidating any existing ones.
/// </summary>
/// <param name="OrganizationId">The organization ID.</param>
/// <param name="UserId">The user ID.</param>
/// <param name="IpAddress">The IP address of the request.</param>
/// <param name="UserAgent">The User-Agent of the request.</param>
public record RegenerateRecoveryCodesCommand(
    string OrganizationId,
    string UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<EnableTotpResponse>>;
