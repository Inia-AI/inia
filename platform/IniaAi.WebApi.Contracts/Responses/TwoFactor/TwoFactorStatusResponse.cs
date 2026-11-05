namespace IniaAi.WebApi.Contracts.Responses.TwoFactor;

/// <summary>
/// Response containing the user's 2FA status and configuration.
/// </summary>
public class TwoFactorStatusResponse
{
    /// <summary>
    /// Whether 2FA is currently enabled for the user.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the user has set up an authenticator app.
    /// </summary>
    public bool HasAuthenticator { get; set; }

    /// <summary>
    /// Number of recovery codes remaining.
    /// </summary>
    public int RecoveryCodesRemaining { get; set; }

    /// <summary>
    /// List of available MFA methods for this user.
    /// </summary>
    public List<string> AvailableMethods { get; set; } = [];
}
