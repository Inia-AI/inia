using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Two-factor recovery codes are stored as salted password hashes, never in clear (ASVS 6.5.2: a
/// lookup secret under 112 bits of entropy). A code works once. A code stored in clear before the
/// change still works, and the user's remaining codes are stored hashed from then on.
/// </summary>
public sealed class RecoveryCodeHashingUserStoreTests : IDisposable
{
    private readonly InMemoryIdentityHarness _identity = new();

    public void Dispose()
    {
        _identity.Dispose();
    }

    [Fact]
    public async Task NewCodes_AreStoredHashed_NotInClear()
    {
        User user = await _identity.CreateUserAsync("codes@test.com");

        List<string> codes = [.. (await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!];

        string stored = await StoredCodesAsync(user);
        Assert.All(codes, code => Assert.DoesNotContain(code, stored, StringComparison.Ordinal));
        Assert.Equal(10, await _identity.UserManager.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task ACode_WorksOnce()
    {
        User user = await _identity.CreateUserAsync("once@test.com");
        string code = (await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 3))!.First();

        IdentityResult first = await _identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        IdentityResult second = await _identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, code);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(2, await _identity.UserManager.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task AWrongCode_RedeemsNothing()
    {
        User user = await _identity.CreateUserAsync("wrong@test.com");
        _ = await _identity.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 2);

        IdentityResult result = await _identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, "BCDFG-HJKMN");

        Assert.False(result.Succeeded);
        Assert.Equal(2, await _identity.UserManager.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task ACodeStoredInClearBeforeTheChange_StillWorks_AndTheRestAreHashedThen()
    {
        User user = await _identity.CreateUserAsync("legacy@test.com");
        _ = await _identity.UserManager.SetAuthenticationTokenAsync(user, RecoveryCodeHashingUserStore.INTERNAL_LOGIN_PROVIDER, RecoveryCodeHashingUserStore.RECOVERY_CODE_TOKEN_NAME, "BCDFG-HJKMN;PQRTV-WXY23");

        IdentityResult redeemed = await _identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, "BCDFG-HJKMN");

        Assert.True(redeemed.Succeeded);
        Assert.DoesNotContain("PQRTV-WXY23", await StoredCodesAsync(user), StringComparison.Ordinal);
        Assert.True((await _identity.UserManager.RedeemTwoFactorRecoveryCodeAsync(user, "PQRTV-WXY23")).Succeeded);
        Assert.Equal(0, await _identity.UserManager.CountRecoveryCodesAsync(user));
    }

    private async Task<string> StoredCodesAsync(User user)
    {
        return await _identity.Context.UserTokens.AsNoTracking()
            .Where(token => token.UserId == user.Id && token.LoginProvider == RecoveryCodeHashingUserStore.INTERNAL_LOGIN_PROVIDER && token.Name == RecoveryCodeHashingUserStore.RECOVERY_CODE_TOKEN_NAME)
            .Select(token => token.Value!)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
