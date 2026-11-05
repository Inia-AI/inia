namespace IniaAi.WebApi.Contracts.Requests.User;

/// <summary>
/// Request payload for changing the authenticated user's password.
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>
    /// Gets or sets the current password of the user.
    /// </summary>
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the new password to be set.
    /// </summary>
    public string NewPassword { get; set; } = string.Empty;
}
