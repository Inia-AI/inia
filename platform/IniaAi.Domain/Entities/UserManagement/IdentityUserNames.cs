using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The user names Identity accepts: letters, digits and <c>- . _ @ +</c>, the default character
/// rule both apps keep. Identity refuses every update to an account whose stored user name breaks
/// it, so every path that writes a user name outside UserManager checks it here.
/// </summary>
public static class IdentityUserNames
{
    private static readonly string _allowedCharacters = new UserOptions().AllowedUserNameCharacters;

    public static bool Accepts(string? userName)
    {
        return !string.IsNullOrEmpty(userName) && userName.All(_allowedCharacters.Contains);
    }
}
