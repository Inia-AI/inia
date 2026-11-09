using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Encodings.Web;

namespace IniaAi.Application.Commands.TwoFactor.SetupTotp;

/// <summary>
/// Handles the TOTP setup command by generating a new authenticator key.
/// </summary>
public class SetupTotpCommandHandler : IRequestHandler<SetupTotpCommand, ErrorOr<TotpSetupResponse>>
{
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    private readonly UserManager<User> _userManager;
    private readonly UrlEncoder _urlEncoder;
    private readonly IAuthenticationAuditLog _audit;
    private readonly ILogger<SetupTotpCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupTotpCommandHandler"/> class.
    /// </summary>
    public SetupTotpCommandHandler(
        UserManager<User> userManager,
        UrlEncoder urlEncoder,
        IAuthenticationAuditLog audit,
        ILogger<SetupTotpCommandHandler> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _urlEncoder = urlEncoder ?? throw new ArgumentNullException(nameof(urlEncoder));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<TotpSetupResponse>> Handle(SetupTotpCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found");
        }

        // Reset and get new authenticator key
        IdentityResult reset = await _userManager.ResetAuthenticatorKeyAsync(user);
        string? unformattedKey = reset.Succeeded ? await _userManager.GetAuthenticatorKeyAsync(user) : null;

        if (string.IsNullOrEmpty(unformattedKey))
        {
            _logger.LogError("Failed to generate authenticator key for user {UserId}", request.UserId);
            return Error.Failure("TwoFactor.KeyGenerationFailed", "Failed to generate authenticator key");
        }

        string email = await _userManager.GetEmailAsync(user) ?? user.UserName ?? "user";

        // Generate QR code URI
        string authenticatorUri = GenerateQrCodeUri(email, unformattedKey);

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.TwoFactorSetupStarted, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return new TotpSetupResponse
        {
            SharedKey = FormatKey(unformattedKey),
            AuthenticatorUri = authenticatorUri,
        };
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            AuthenticatorUriFormat,
            _urlEncoder.Encode("Inia AI"),
            _urlEncoder.Encode(email),
            unformattedKey);
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            _ = result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < unformattedKey.Length)
        {
            _ = result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToUpperInvariant();
    }
}
