using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The one password rule. Identity applies it to a user's own change and reset; the admin app's
/// Create User and Reset Password, the CSV import and the seeder set a password directly, not
/// through UserManager, so they check it here.
/// </summary>
public static class PasswordPolicy
{
    public const int MINIMUM_LENGTH = 8;

    public const string REQUIRED = "Password is required!";
    private const string NO_UPPERCASE = "Password must contain an uppercase letter (A to Z)";
    private const string NO_LOWERCASE = "Password must contain a lowercase letter (a to z)";
    private const string NO_DIGIT = "Password must contain a digit (0 to 9)";

    private static string TooShort => $"Password must be at least {MINIMUM_LENGTH} characters";

    public static string Description => $"at least {MINIMUM_LENGTH} characters, with an uppercase letter, a lowercase letter and a digit";

    public static void Apply(PasswordOptions options)
    {
        options.RequiredLength = MINIMUM_LENGTH;
        options.RequireDigit = true;
        options.RequireUppercase = true;
        options.RequireLowercase = true;
        options.RequireNonAlphanumeric = false;
    }

    public static bool Accepts(string password)
    {
        return !Problems(password).Any();
    }

    public static IEnumerable<string> Problems(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            yield return REQUIRED;
            yield break;
        }

        if (password.Length < MINIMUM_LENGTH)
        {
            yield return TooShort;
        }

        if (!password.Any(char.IsAsciiLetterUpper))
        {
            yield return NO_UPPERCASE;
        }

        if (!password.Any(char.IsAsciiLetterLower))
        {
            yield return NO_LOWERCASE;
        }

        if (!password.Any(char.IsAsciiDigit))
        {
            yield return NO_DIGIT;
        }
    }
}
