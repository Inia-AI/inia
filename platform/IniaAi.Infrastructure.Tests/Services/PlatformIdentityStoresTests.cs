using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Every host that takes the platform's Identity stores gets the password rule and the reset rule
/// with them, so a new host or a new reset path cannot leave either out.
/// </summary>
public sealed class PlatformIdentityStoresTests : IDisposable
{
    private const string UNCOMMON = "QuietMaple42";

    private readonly InMemoryIdentityHarness _identity = new();

    public void Dispose()
    {
        _identity.Dispose();
    }

    [Theory]
    [InlineData("password123", CommonPasswordValidator.CODE)]
    [InlineData("Maple42", nameof(IdentityErrorDescriber.PasswordTooShort))]
    public async Task APasswordTheRuleRefuses_IsRefused(string password, string code)
    {
        IdentityResult created = await _identity.UserManager.CreateAsync(NewUser(), password);

        Assert.Contains(code, created.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData(UNCOMMON)]
    [InlineData("quietmaple")]
    public async Task APasswordOfEightOrMoreUncommonCharacters_IsTaken_WithNoKindOfCharacterRequired(string password)
    {
        IdentityResult created = await _identity.UserManager.CreateAsync(NewUser(), password);

        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Code)));
    }

    [Fact]
    public async Task AResetToken_SetsNoPasswordOnAnAccountThatHasNone()
    {
        User withoutPassword = await _identity.CreateUserAsync($"{Guid.NewGuid():N}@example.com");
        string token = await _identity.UserManager.GeneratePasswordResetTokenAsync(withoutPassword);

        IdentityResult reset = await _identity.UserManager.ResetPasswordAsync(withoutPassword, token, UNCOMMON);

        Assert.Contains(nameof(IdentityErrorDescriber.InvalidToken), reset.Errors.Select(error => error.Code));
        Assert.False(await _identity.UserManager.HasPasswordAsync(withoutPassword));
    }

    [Fact]
    public async Task AResetToken_StillResetsAnAccountThatHasAPassword()
    {
        User user = NewUser();
        Assert.True((await _identity.UserManager.CreateAsync(user, UNCOMMON)).Succeeded);
        string token = await _identity.UserManager.GeneratePasswordResetTokenAsync(user);

        IdentityResult reset = await _identity.UserManager.ResetPasswordAsync(user, token, "Second-Test-Passw0rd"); // gitleaks:allow

        Assert.True(reset.Succeeded, string.Join(", ", reset.Errors.Select(error => error.Code)));
    }

    private static User NewUser()
    {
        string email = $"{Guid.NewGuid():N}@example.com";

        return new User { UserName = email, Email = email, EmailConfirmed = true };
    }
}
