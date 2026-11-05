namespace IniaAi.WebApi.Contracts.Requests.TwoFactor;

/// <summary>
/// Request to disable two-factor authentication.
/// </summary>
public class DisableTotpRequest
{
    /// <summary>
    /// The 6-digit verification code from the authenticator app to confirm disable.
    /// </summary>
    public string Code { get; set; } = string.Empty;
}
