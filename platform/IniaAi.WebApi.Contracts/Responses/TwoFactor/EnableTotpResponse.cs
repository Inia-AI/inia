namespace IniaAi.WebApi.Contracts.Responses.TwoFactor;

/// <summary>
/// Response after successfully enabling TOTP, containing recovery codes.
/// </summary>
public class EnableTotpResponse
{
    /// <summary>
    /// The generated recovery codes. These should be stored securely by the user.
    /// </summary>
    public string[] RecoveryCodes { get; set; } = [];
}
