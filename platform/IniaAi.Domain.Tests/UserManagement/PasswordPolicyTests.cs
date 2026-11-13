using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Tests.UserManagement;

/// <summary>
/// One password rule for every path that sets a password: a user's own change and reset through
/// Identity, and an admin's Create User, Reset Password and CSV import through the rule's own check.
/// </summary>
public class PasswordPolicyTests
{
    private const string SEVEN_CHARACTERS = "quietmp";
    private const string ONE_KIND_OF_CHARACTER = "quiet maple lantern";
    private const string TOO_COMMON = "This password is one of the most commonly used. Choose a different one.";

    [Fact]
    public void ASevenCharacterPassword_IsRefusedForItsLengthAlone()
    {
        Assert.Equal(["Password must be at least 8 characters"], PasswordPolicy.Problems(SEVEN_CHARACTERS));
        Assert.False(PasswordPolicy.Accepts(SEVEN_CHARACTERS));
    }

    [Fact]
    public void ALongPasswordOfOneKindOfCharacter_Passes()
    {
        Assert.Empty(PasswordPolicy.Problems(ONE_KIND_OF_CHARACTER));
        Assert.True(PasswordPolicy.Accepts(ONE_KIND_OF_CHARACTER));
    }

    [Theory]
    [InlineData("password1")]
    [InlineData("Password1")]
    [InlineData("QWERTYUIOP")]
    public void ACommonlyUsedPassword_IsRefused_InAnyCase(string password)
    {
        Assert.Equal([TOO_COMMON], PasswordPolicy.Problems(password));
    }

    [Fact]
    public void TheCommonList_HoldsAtLeastTheTop3000PasswordsLongEnoughToPassTheLength()
    {
        Assert.True(CommonPasswords.Count >= 3000, $"{CommonPasswords.Count} common passwords");
    }

    [Fact]
    public void IdentityIsGivenTheSameMinimum_AndAsksForNoKindOfCharacter()
    {
        PasswordOptions options = new();

        PasswordPolicy.Apply(options);

        Assert.Equal(8, options.RequiredLength);
        Assert.False(options.RequireUppercase || options.RequireLowercase || options.RequireDigit || options.RequireNonAlphanumeric);
    }

    [Fact]
    public async Task IdentityRefusesACommonPassword_ThroughTheValidator()
    {
        IdentityResult result = await new CommonPasswordValidator().ValidateAsync(null!, new User(), "iloveyou");

        IdentityError error = Assert.Single(result.Errors);
        Assert.Equal((CommonPasswordValidator.CODE, TOO_COMMON), (error.Code, error.Description));
    }

    [Fact]
    public async Task IdentityLetsAnUncommonPasswordThrough_TheValidator()
    {
        IdentityResult result = await new CommonPasswordValidator().ValidateAsync(null!, new User(), ONE_KIND_OF_CHARACTER);

        Assert.True(result.Succeeded);
    }
}
