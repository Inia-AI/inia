using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Tests.UserManagement;

/// <summary>
/// One password rule for every path that sets a password: a user's own change and reset through
/// Identity, and an admin's Create User, Reset Password and CSV import through the rule's own check.
/// </summary>
public class PasswordPolicyTests
{
    private const string SEVEN_CHARACTERS = "Abcdef1";
    private const string EIGHT_CHARACTERS = "Abcdefg1";

    [Fact]
    public void ASevenCharacterPassword_IsRefusedForItsLengthAlone()
    {
        Assert.Equal(["Password must be at least 8 characters"], PasswordPolicy.Problems(SEVEN_CHARACTERS));
        Assert.False(PasswordPolicy.Accepts(SEVEN_CHARACTERS));
    }

    [Fact]
    public void AnEightCharacterPasswordWithEachKindOfCharacter_Passes()
    {
        Assert.Empty(PasswordPolicy.Problems(EIGHT_CHARACTERS));
        Assert.True(PasswordPolicy.Accepts(EIGHT_CHARACTERS));
    }

    [Fact]
    public void EachMissingKindOfCharacter_IsNamed()
    {
        Assert.Equal(
            [
                "Password must contain an uppercase letter (A to Z)",
                "Password must contain a digit (0 to 9)",
            ],
            PasswordPolicy.Problems("abcdefgh"));
        Assert.Equal(["Password must contain a lowercase letter (a to z)"], PasswordPolicy.Problems("ABCDEFG1"));
    }

    [Fact]
    public void IdentityIsGivenTheSameMinimum()
    {
        PasswordOptions options = new();

        PasswordPolicy.Apply(options);

        Assert.Equal(8, options.RequiredLength);
        Assert.True(options.RequireUppercase && options.RequireLowercase && options.RequireDigit);
        Assert.False(options.RequireNonAlphanumeric);
    }
}
