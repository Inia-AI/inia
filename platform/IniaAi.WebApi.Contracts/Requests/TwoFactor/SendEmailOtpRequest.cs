namespace IniaAi.WebApi.Contracts.Requests.TwoFactor;

/// <summary>
/// Request to send an email OTP code during MFA verification.
/// </summary>
public class SendEmailOtpRequest
{
    /// <summary>
    /// The MFA token received from the initial login attempt.
    /// </summary>
    public string MfaToken { get; set; } = string.Empty;
}
