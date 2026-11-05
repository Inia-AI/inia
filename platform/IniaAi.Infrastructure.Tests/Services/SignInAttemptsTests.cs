using IniaAi.Application.Auditing;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// A correct password opens a second-factor step, and every guess in that step counts toward the
/// same lockout as a wrong password, the way ASP.NET Core's SignInManager counts them. Every outcome
/// leaves one authentication audit record.
/// </summary>
public sealed class SignInAttemptsTests : IDisposable
{
    private const string WRONG_CODE = "000000";
    private const string PASSWORD = "Correct-horse-battery-1";
    private const string CLIENT = "203.0.113.7";
    private const int LOCKOUT_THRESHOLD = 5;

    private readonly InMemoryIdentityHarness _identity = new();
    private readonly CapturingLogger<LoggingAuthenticationAuditLog> _auditLog = new();
    private readonly CapturingLogger<SignInAttempts> _log = new();
    private readonly SignInAttempts _attempts;

    public SignInAttemptsTests()
    {
        _attempts = new SignInAttempts(_identity.UserManager, new LoggingAuthenticationAuditLog(_auditLog), _log);
    }

    public void Dispose()
    {
        _identity.Dispose();
    }

    [Fact]
    public async Task FiveWrongCodesAcrossFreshSignIns_LockTheAccount()
    {
        User user = await CreateTwoFactorUserAsync();
        SecondFactorOutcome last = SecondFactorOutcome.WrongCode;

        for (int attempt = 0; attempt < LOCKOUT_THRESHOLD; attempt++)
        {
            await _attempts.PasswordAcceptedAsync(user, secondFactorPending: true, CLIENT);
            last = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.TOTP, WRONG_CODE, CLIENT);
        }

        Assert.Equal(SecondFactorOutcome.LockedOut, last);
        Assert.True(await _identity.UserManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task ALockedOutAccount_IsRefusedBeforeItsCodeIsChecked()
    {
        User user = await CreateTwoFactorUserAsync();
        string recoveryCode = (await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        _ = await _identity.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));

        SecondFactorOutcome outcome = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.RECOVERY, recoveryCode, CLIENT);

        Assert.Equal(SecondFactorOutcome.LockedOut, outcome);
        Assert.Equal(1, await _identity.UserManager.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task ACorrectPasswordWithASecondFactorPending_KeepsTheFailureCount()
    {
        User user = await CreateTwoFactorUserAsync();
        _ = await _identity.UserManager.AccessFailedAsync(user);

        await _attempts.PasswordAcceptedAsync(user, secondFactorPending: true, CLIENT);

        Assert.Equal(1, await _identity.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task ACorrectPasswordWithNoSecondFactor_ClearsTheFailureCount()
    {
        User user = await CreateTwoFactorUserAsync();
        _ = await _identity.UserManager.AccessFailedAsync(user);

        await _attempts.PasswordAcceptedAsync(user, secondFactorPending: false, CLIENT);

        Assert.Equal(0, await _identity.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task ACorrectCode_ClearsTheFailureCount()
    {
        User user = await CreateTwoFactorUserAsync();
        string recoveryCode = (await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        _ = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.TOTP, WRONG_CODE, CLIENT);

        SecondFactorOutcome outcome = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.RECOVERY, recoveryCode, CLIENT);

        Assert.Equal(SecondFactorOutcome.Verified, outcome);
        Assert.Equal(0, await _identity.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task AWrongCodeBelowTheThreshold_IsAWrongCode()
    {
        User user = await CreateTwoFactorUserAsync();

        SecondFactorOutcome outcome = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.TOTP, WRONG_CODE, CLIENT);

        Assert.Equal(SecondFactorOutcome.WrongCode, outcome);
        Assert.Equal(1, await _identity.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task ALockedOutAccountWithItsCorrectPassword_IsRefusedLikeAWrongPassword()
    {
        User user = await CreateTwoFactorUserAsync();
        _ = await _identity.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));

        PasswordOutcome lockedOut = await _attempts.CheckPasswordAsync(user, PASSWORD, CLIENT);
        PasswordOutcome unknown = await _attempts.CheckPasswordAsync(null, PASSWORD, CLIENT);

        Assert.Equal(PasswordOutcome.Refused, lockedOut);
        Assert.Equal(unknown, lockedOut);
    }

    [Fact]
    public async Task ACorrectPasswordForAnAccountThatMayNotSignIn_IsAuditedButNotCounted()
    {
        User user = await CreateTwoFactorUserAsync();

        await _attempts.AccountRefusedAsync(user, CLIENT);

        CapturedLogEntry refused = Assert.Single(_auditLog.Entries);
        Assert.Equal(AuthenticationAuditKind.SignInRefused, refused.Value("AuditKind"));
        Assert.Equal(AuthenticationAuditMethods.PASSWORD_SIGN_IN, refused.Value("AuditMethod"));
        Assert.Equal(0, await _identity.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task AWrongPassword_CountsAndIsAudited()
    {
        User user = await CreateTwoFactorUserAsync();

        PasswordOutcome outcome = await _attempts.CheckPasswordAsync(user, "not-the-password", CLIENT);

        Assert.Equal(PasswordOutcome.Refused, outcome);
        Assert.Equal(1, await _identity.UserManager.GetAccessFailedCountAsync(user));
        CapturedLogEntry record = Assert.Single(_auditLog.Entries);
        Assert.Equal(AuthenticationAuditKind.SignInRefused, record.Value("AuditKind"));
        Assert.Equal(user.Id, record.Value("UserId"));
        Assert.Equal(CLIENT, record.Value("ClientAddress"));
        Assert.DoesNotContain(user.Email!, record.Message);
    }

    [Fact]
    public async Task EachOutcome_LeavesOneAuditRecord()
    {
        User user = await CreateTwoFactorUserAsync();
        string recoveryCode = (await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();

        _ = await _attempts.CheckPasswordAsync(user, PASSWORD, CLIENT);
        await _attempts.PasswordAcceptedAsync(user, secondFactorPending: true, CLIENT);
        _ = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.TOTP, WRONG_CODE, CLIENT);
        _ = await _attempts.VerifySecondFactorAsync(user, AuthenticationAuditMethods.RECOVERY, recoveryCode, CLIENT);

        Assert.Equal(
            [AuthenticationAuditKind.SecondFactorRequired, AuthenticationAuditKind.SignInRefused, AuthenticationAuditKind.SignInSucceeded],
            _auditLog.Entries.Select(entry => (AuthenticationAuditKind)entry.Value("AuditKind")!));
    }

    [Fact]
    public async Task FailureCountIdentityRefusesToSave_IsLoggedAsAnError()
    {
        User user = await CreateTwoFactorUserAsync();
        user.UserName = "Example Admin";
        _ = await _identity.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await _attempts.CheckPasswordAsync(user, "not-the-password", CLIENT);

        CapturedLogEntry refused = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(user.Id, refused.Value("UserId"));
        Assert.Contains("InvalidUserName", refused.Message);
    }

    private async Task<User> CreateTwoFactorUserAsync()
    {
        User user = await _identity.CreateUserAsync($"analyst-{Guid.NewGuid():N}@example.com");
        _ = await _identity.UserManager.AddPasswordAsync(user, PASSWORD);
        _ = await _identity.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await _identity.UserManager.SetTwoFactorEnabledAsync(user, true);
        return user;
    }
}
