using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Refuses a commonly used password in Identity's own checks: a user's change and reset, in both
/// apps. Identity's options can set only a length and kinds of character.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<User>
{
    public const string CODE = "PasswordTooCommon";

    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        return Task.FromResult(password is not null && CommonPasswords.Contains(password)
            ? IdentityResult.Failed(new IdentityError { Code = CODE, Description = PasswordPolicy.TOO_COMMON })
            : IdentityResult.Success);
    }
}
