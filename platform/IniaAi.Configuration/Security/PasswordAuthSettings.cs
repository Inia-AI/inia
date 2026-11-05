namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for password-based authentication.
/// </summary>
public class PasswordAuthSettings
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Authentication:Password";

    /// <summary>
    /// Gets or sets whether password authentication is enabled.
    /// Defaults to true for backwards compatibility.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of failed access attempts before account lockout.
    /// Defaults to 5 attempts.
    /// </summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the lockout duration in minutes after max failed attempts.
    /// Defaults to 15 minutes.
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;
}
