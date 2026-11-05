using ErrorOr;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.TwoFactor.RegenerateRecoveryCodes;

/// <summary>
/// Handles the command to regenerate recovery codes.
/// </summary>
public class RegenerateRecoveryCodesCommandHandler : IRequestHandler<RegenerateRecoveryCodesCommand, ErrorOr<EnableTotpResponse>>
{
    private readonly UserManager<User> _userManager;
    private readonly TwoFactorSettings _settings;
    private readonly IAuthenticationAuditLog _audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegenerateRecoveryCodesCommandHandler"/> class.
    /// </summary>
    public RegenerateRecoveryCodesCommandHandler(
        UserManager<User> userManager,
        IOptions<TwoFactorSettings> settings,
        IAuthenticationAuditLog audit)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<EnableTotpResponse>> Handle(RegenerateRecoveryCodesCommand request, CancellationToken cancellationToken)
    {
        User? user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found");
        }

        bool isTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        if (!isTwoFactorEnabled)
        {
            return Error.Validation("TwoFactor.NotEnabled", "Two-factor authentication is not enabled");
        }

        IEnumerable<string>? recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, _settings.RecoveryCodeCount);
        if (recoveryCodes is null)
        {
            return Error.Failure("TwoFactor.RecoveryCodesFailed", "New recovery codes could not be saved for this account");
        }

        await _audit.RecordAsync(new AuthenticationAuditEvent(AuthenticationAuditKind.RecoveryCodesRegenerated, request.UserId, ClientAddress: request.IpAddress), cancellationToken);

        return new EnableTotpResponse
        {
            RecoveryCodes = [.. recoveryCodes],
        };
    }
}
