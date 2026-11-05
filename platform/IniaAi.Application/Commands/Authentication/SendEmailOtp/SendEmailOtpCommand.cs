using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Authentication.SendEmailOtp;

/// <summary>
/// Command to send an email OTP code for 2FA verification.
/// </summary>
/// <param name="MfaToken">The MFA token from the login flow.</param>
/// <param name="ClientIpAddress">The client's IP address for validation.</param>
public record SendEmailOtpCommand(
    string MfaToken,
    string? ClientIpAddress) : IRequest<ErrorOr<Success>>;
