namespace IniaAi.WebApi.Contracts.Requests.User;

/// <summary>
/// Request payload for setting a new password with the token from a reset mail.
/// </summary>
public class ResetPasswordRequest
{
    /// <summary>
    /// Gets or sets the address the reset mail went to.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token from the reset link.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the new password.
    /// </summary>
    public string NewPassword { get; set; } = string.Empty;
}
