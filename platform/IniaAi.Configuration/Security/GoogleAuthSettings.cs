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

    /// <summary>The configuration key of the client id, for a message that names it.</summary>
    public const string CLIENT_ID_KEY = $"{SectionName}:{nameof(ClientId)}";

    /// <summary>Why the API refuses to start with Google sign-in on and no client id.</summary>
    public const string CLIENT_ID_REFUSAL = $"{CLIENT_ID_KEY} is required when Google sign-in is on: a Google token is accepted only when it was issued to this client id.";

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

    /// <summary>
    /// Gets or sets whether Google sign-in accepts only an address Google proves: a Gmail address, or
    /// one in the Workspace domain its token names. Any other address counts as unverified, and its
    /// holder signs in another way. Defaults to false for backwards compatibility.
    /// </summary>
    public bool ProvenAddressesOnly { get; set; } = false;

    /// <summary>Whether Google sign-in is off, or on with a client id to check tokens against.</summary>
    public static bool HasClientIdWhenEnabled(GoogleAuthSettings settings)
    {
        return !settings.Enabled || !string.IsNullOrWhiteSpace(settings.ClientId);
    }
}
