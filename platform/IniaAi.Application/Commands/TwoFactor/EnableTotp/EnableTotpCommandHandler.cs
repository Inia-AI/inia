using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.TwoFactor.EnableTotp;

/// <summary>
/// Handles the command to enable TOTP-based 2FA.
/// </summary>
public class EnableTotpCommandHandler : IRequestHandler<EnableTotpCommand, ErrorOr<EnableTotpResponse>>
{
    private readonly UserManager<User> _userManager;
    private readonly TwoFactorSettings _settings;
    private readonly IAuthenticationAuditLog _audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnableTotpCommandHandler"/> class.
    /// </summary>
    public EnableTotpCommandHandler(
        UserManager<User> userManager,
        IOptions<TwoFactorSettings> settings,
        IAuthenticationAuditLog audit)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<EnableTotpResponse>> Handle(EnableTotpCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found");
        }

        // Clean and verify the code
        string code = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        bool isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!isValid)
        {
            return Error.Validation("TwoFactor.InvalidCode", "Invalid verification code");
        }

        IdentityResult enabled = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return Error.Failure("TwoFactor.EnableFailed", "Two-factor authentication could not be turned on for this account");
        }

        IEnumerable<string>? recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, _settings.RecoveryCodeCount);

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.TwoFactorEnabled, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return new EnableTotpResponse
        {
            RecoveryCodes = recoveryCodes?.ToArray() ?? [],
        };
    }
}
