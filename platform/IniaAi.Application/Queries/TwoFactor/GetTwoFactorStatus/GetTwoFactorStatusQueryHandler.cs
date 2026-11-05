using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Responses.TwoFactor;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Application.Queries.TwoFactor.GetTwoFactorStatus;

/// <summary>
/// Handles the query to get the current 2FA status for a user.
/// </summary>
public class GetTwoFactorStatusQueryHandler : IRequestHandler<GetTwoFactorStatusQuery, ErrorOr<TwoFactorStatusResponse>>
{
    private readonly UserManager<User> _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTwoFactorStatusQueryHandler"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    public GetTwoFactorStatusQueryHandler(UserManager<User> userManager)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<TwoFactorStatusResponse>> Handle(GetTwoFactorStatusQuery request, CancellationToken cancellationToken)
    {
        User? user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Error.NotFound("User.NotFound", "User not found");
        }

        bool hasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) is not null;
        bool is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        int recoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);

        var availableMethods = new List<string>();
        if (hasAuthenticator)
        {
            availableMethods.Add("totp");
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            availableMethods.Add("email");
        }

        if (recoveryCodesLeft > 0)
        {
            availableMethods.Add("recovery");
        }

        return new TwoFactorStatusResponse
        {
            IsEnabled = is2faEnabled,
            HasAuthenticator = hasAuthenticator,
            RecoveryCodesRemaining = recoveryCodesLeft,
            AvailableMethods = availableMethods,
        };
    }
}
