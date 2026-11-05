namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for Google OAuth authentication.
/// </summary>
public class GoogleAuthSettings
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Authentication:Google";

    /// <summary>
    /// Gets or sets the Google OAuth Client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Google OAuth Client Secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether Google authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the allowed hosted domains for Google Workspace users.
    /// If empty, all Google accounts are allowed.
    /// Example: ["inia.ai", "company.com"]
    /// </summary>
    public List<string> AllowedDomains { get; set; } = [];
}
