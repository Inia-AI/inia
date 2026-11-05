using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Account mutations the administration screen applies to a user it loaded earlier. The screen's
/// user list is a snapshot from page load, and any change since (a failed login rotates the
/// concurrency stamp) makes an update from that snapshot fail with a concurrency error, so each
/// mutation first refreshes the tracked user from the database.
/// </summary>
public sealed class UserAccountAdminService
{
    public const string USER_NOT_FOUND_CODE = "UserNotFound";
    public const string PASSWORD_REFUSED_CODE = "PasswordRefused";
    private const string USER_NOT_FOUND_DESCRIPTION = "The user no longer exists.";

    private readonly UserManager<User> _userManager;
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IUserSessionRevoker _sessionRevoker;

    public UserAccountAdminService(UserManager<User> userManager, IUserDataUnitOfWork unitOfWork, IUserSessionRevoker sessionRevoker)
    {
        _userManager = userManager;
        _unitOfWork = unitOfWork;
        _sessionRevoker = sessionRevoker;
    }

    /// <summary>
    /// Replaces the user's password, rotates the security stamp and revokes the user's API tokens,
    /// so existing sessions end. The password is set directly, not through UserManager, so the
    /// password rule is checked here.
    /// </summary>
    public async Task<IdentityResult> ResetPasswordAsync(User user, string newPassword)
    {
        IdentityError[] problems = [.. PasswordPolicy.Problems(newPassword)
            .Select(problem => new IdentityError { Code = PASSWORD_REFUSED_CODE, Description = problem })];
        if (problems.Length > 0)
        {
            return IdentityResult.Failed(problems);
        }

        if (!await _unitOfWork.ReloadAsync(user))
        {
            return UserNotFound();
        }

        string? previousPasswordHash = user.PasswordHash;
        user.SetPassword(newPassword);

        IdentityResult result = await _userManager.UpdateSecurityStampAsync(user);

        if (!result.Succeeded)
        {
            user.PasswordHash = previousPasswordHash;
            await _unitOfWork.UntrackAsync(user);

            return result;
        }

        await _sessionRevoker.RevokeAllAsync(user.Id);

        return result;
    }

    /// <summary>
    /// Locks the user out administratively and rotates the security stamp so existing sessions end.
    /// </summary>
    public async Task<IdentityResult> DisableAsync(User user)
    {
        if (!await _unitOfWork.ReloadAsync(user))
        {
            return UserNotFound();
        }

        user.Disable();

        IdentityResult result = await UntrackOnFailureAsync(user, await _userManager.UpdateSecurityStampAsync(user));
        if (result.Succeeded)
        {
            await _sessionRevoker.RevokeAllAsync(user.Id);
        }

        return result;
    }

    /// <summary>
    /// Lifts an administrative lockout.
    /// </summary>
    public async Task<IdentityResult> EnableAsync(User user)
    {
        if (!await _unitOfWork.ReloadAsync(user))
        {
            return UserNotFound();
        }

        user.Enable();

        return await UntrackOnFailureAsync(user, await _userManager.UpdateAsync(user));
    }

    private async Task<IdentityResult> UntrackOnFailureAsync(User user, IdentityResult result)
    {
        if (!result.Succeeded)
        {
            await _unitOfWork.UntrackAsync(user);
        }

        return result;
    }

    private static IdentityResult UserNotFound()
    {
        return IdentityResult.Failed(new IdentityError
        {
            Code = USER_NOT_FOUND_CODE,
            Description = USER_NOT_FOUND_DESCRIPTION,
        });
    }
}
