namespace IniaAi.WebApi.Contracts.Requests.TwoFactor;

/// <summary>
/// Request to enable TOTP-based two-factor authentication.
/// </summary>
public class EnableTotpRequest
{
    /// <summary>
    /// The 6-digit verification code from the authenticator app.
    /// </summary>
    public string Code { get; set; } = string.Empty;
}
