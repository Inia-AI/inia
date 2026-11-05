namespace IniaAi.WebApi.Contracts.Requests.User;

/// <summary>
/// Request payload for initiating the forgot password process.
/// </summary>
public class ForgotPasswordRequest
{
    /// <summary>
    /// Gets or sets the user's registered email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
