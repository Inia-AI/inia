using IniaAi.Application.Auditing;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The outcome of one password check. A locked-out account and a wrong password share
/// <see cref="Refused"/>, so the answer does not reveal which accounts exist.
/// </summary>
public enum PasswordOutcome
{
    Accepted,
    Refused,
}

/// <summary>
/// The outcome of one second-factor guess.
/// </summary>
public enum SecondFactorOutcome
{
    Verified,
    WrongCode,
    LockedOut,
}

/// <summary>
/// Counts sign-in failures the way ASP.NET Core's <c>SignInManager</c> does: a correct password
/// clears the count only when no second factor is pending, and each wrong second-factor code
/// counts toward the same lockout. Without this, a fresh password sign-in would reset the count
/// before every code guess, and code guessing would have no bound. Every outcome goes to the
/// authentication audit.
/// </summary>
public class SignInAttempts(UserManager<User> userManager, IAuthenticationAuditLog audit, ILogger<SignInAttempts> logger)
{
    private const string UNKNOWN_METHOD = "unknown";

    private static readonly PasswordHasher<User> _hasher = new();
    private static readonly string _unknownUserHash = _hasher.HashPassword(new User(), Guid.NewGuid().ToString());

    /// <summary>
    /// Checks a password for the account an email named, or for none. Every path runs one hash
    /// check, so the answer takes as long for an unknown or locked-out account as for a real one.
    /// </summary>
    public async Task<PasswordOutcome> CheckPasswordAsync(User? user, string password, string? clientAddress)
    {
        if (user is null)
        {
            _ = _hasher.VerifyHashedPassword(new User(), _unknownUserHash, password);
            await RecordAsync(AuthenticationAuditKind.SignInRefused, null, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
            return PasswordOutcome.Refused;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            _ = await userManager.CheckPasswordAsync(user, password);
            await RecordAsync(AuthenticationAuditKind.SignInLockedOut, user.Id, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
            return PasswordOutcome.Refused;
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            return PasswordOutcome.Accepted;
        }

        _ = await CountFailureAsync(user, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
        return PasswordOutcome.Refused;
    }

    /// <summary>
    /// Records a correct password. The failure count clears only when the sign-in ends here.
    /// </summary>
    public async Task PasswordAcceptedAsync(User user, bool secondFactorPending, string? clientAddress)
    {
        if (secondFactorPending)
        {
            await RecordAsync(AuthenticationAuditKind.SecondFactorRequired, user.Id, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
            return;
        }

        await ClearFailuresAsync(user);
        await RecordAsync(AuthenticationAuditKind.SignInSucceeded, user.Id, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
    }

    /// <summary>
    /// Records a correct password for an account that may not sign in, such as a system user or an
    /// unconfirmed address. A correct password does not count toward the lockout.
    /// </summary>
    public Task AccountRefusedAsync(User user, string? clientAddress)
    {
        return AccountRefusedAsync(user, AuthenticationAuditMethods.PASSWORD_SIGN_IN, clientAddress);
    }

    /// <summary>
    /// Records an account that may not sign in, by the method that vouched for it.
    /// </summary>
    public Task AccountRefusedAsync(User user, string method, string? clientAddress)
    {
        return RecordAsync(AuthenticationAuditKind.SignInRefused, user.Id, method, clientAddress);
    }

    /// <summary>
    /// Checks a second-factor code. A locked-out account is refused before its code is checked,
    /// and a wrong code counts as a failed sign-in.
    /// </summary>
    public async Task<SecondFactorOutcome> VerifySecondFactorAsync(User user, string method, string code, string? clientAddress)
    {
        string lowered = method.ToLowerInvariant();
        string auditMethod = lowered is AuthenticationAuditMethods.TOTP or AuthenticationAuditMethods.EMAIL or AuthenticationAuditMethods.RECOVERY
            ? lowered
            : UNKNOWN_METHOD;

        if (await userManager.IsLockedOutAsync(user))
        {
            await RecordAsync(AuthenticationAuditKind.SignInLockedOut, user.Id, auditMethod, clientAddress);
            return SecondFactorOutcome.LockedOut;
        }

        if (await CodeIsValidAsync(user, auditMethod, code.Replace(" ", "")))
        {
            await ClearFailuresAsync(user);
            await RecordAsync(AuthenticationAuditKind.SignInSucceeded, user.Id, auditMethod, clientAddress);
            return SecondFactorOutcome.Verified;
        }

        bool lockedOut = await CountFailureAsync(user, auditMethod, clientAddress);
        return lockedOut ? SecondFactorOutcome.LockedOut : SecondFactorOutcome.WrongCode;
    }

    private async Task<bool> CountFailureAsync(User user, string method, string? clientAddress)
    {
        LogRefusal(await userManager.AccessFailedAsync(user), user);

        bool lockedOut = await userManager.IsLockedOutAsync(user);
        await RecordAsync(lockedOut ? AuthenticationAuditKind.SignInLockedOut : AuthenticationAuditKind.SignInRefused, user.Id, method, clientAddress);
        return lockedOut;
    }

    private async Task ClearFailuresAsync(User user)
    {
        LogRefusal(await userManager.ResetAccessFailedCountAsync(user), user);
    }

    // Identity refuses to save an account whose stored fields break its own rules, such as a user
    // name with a space. The failure count then never moves, so the refusal must not stay silent.
    private void LogRefusal(IdentityResult result, User user)
    {
        if (!result.Succeeded)
        {
            logger.LogError(
                "Identity refused to save the sign-in failure count for user {UserId}: {IdentityErrors}",
                user.Id,
                string.Join(", ", result.Errors.Select(error => error.Code)));
        }
    }

    private async Task<bool> CodeIsValidAsync(User user, string method, string code)
    {
        return method switch
        {
            AuthenticationAuditMethods.TOTP => await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code.Replace("-", "")),
            AuthenticationAuditMethods.EMAIL => await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, code.Replace("-", "")),
            // Identity issues recovery codes with a dash in the middle and compares them as issued.
            AuthenticationAuditMethods.RECOVERY => (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded,
            _ => false,
        };
    }

    private Task RecordAsync(AuthenticationAuditKind kind, string? userId, string method, string? clientAddress)
    {
        return audit.RecordAsync(new AuthenticationAuditEvent(kind, userId, method, clientAddress));
    }
}
