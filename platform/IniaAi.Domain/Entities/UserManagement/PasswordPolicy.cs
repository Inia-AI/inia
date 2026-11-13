using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The one password rule: at least <see cref="MINIMUM_LENGTH"/> characters, and not one of the most
/// commonly used passwords (<see cref="CommonPasswords"/>). It asks for no kind of character, as
/// ASVS 6.2.5 forbids. Identity applies it to a user's own change and reset, through
/// <see cref="Apply"/> and <see cref="CommonPasswordValidator"/>; the admin app's Create User and
/// Reset Password, the CSV import and the seeder set a password directly, not through UserManager,
/// so they check it here.
/// </summary>
public static class PasswordPolicy
{
    public const int MINIMUM_LENGTH = 8;

    public const string REQUIRED = "Password is required!";
    public const string TOO_COMMON = "This password is one of the most commonly used. Choose a different one.";

    private static string TooShort => $"Password must be at least {MINIMUM_LENGTH} characters";

    public static string Description => $"at least {MINIMUM_LENGTH} characters, and not one of the most commonly used passwords";

    public static void Apply(PasswordOptions options)
    {
        options.RequiredLength = MINIMUM_LENGTH;
        options.RequireDigit = false;
        options.RequireUppercase = false;
        options.RequireLowercase = false;
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
            yield break;
        }

        if (CommonPasswords.Contains(password))
        {
            yield return TOO_COMMON;
        }
    }
}
