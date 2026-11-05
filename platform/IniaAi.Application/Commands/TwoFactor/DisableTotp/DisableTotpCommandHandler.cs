using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Application.Commands.TwoFactor.DisableTotp;

/// <summary>
/// Handles the command to disable 2FA.
/// </summary>
public class DisableTotpCommandHandler : IRequestHandler<DisableTotpCommand, ErrorOr<Success>>
{
    private readonly UserManager<User> _userManager;
    private readonly IAuthenticationAuditLog _audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisableTotpCommandHandler"/> class.
    /// </summary>
    public DisableTotpCommandHandler(
        UserManager<User> userManager,
        IAuthenticationAuditLog audit)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Success>> Handle(DisableTotpCommand request, CancellationToken cancellationToken)
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

        IdentityResult disabled = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded)
        {
            return Error.Failure("TwoFactor.DisableFailed", "Two-factor authentication could not be turned off for this account");
        }

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.TwoFactorDisabled, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return Result.Success;
    }
}
