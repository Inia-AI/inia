namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Who may reset a password: an account that already has one. A reset on an account that signs in
/// only by Google, OIDC or an email code would give it a second way in, and a system account has
/// no password to reset.
/// </summary>
public static class PasswordReset
{
    public static bool IsOpenTo(User user)
    {
        return !user.IsSystem && user.PasswordHash is not null;
    }
}
