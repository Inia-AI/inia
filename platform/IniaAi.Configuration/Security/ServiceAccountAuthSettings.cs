namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for the service account's client credentials grant.
/// </summary>
public class ServiceAccountAuthSettings
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Authentication:ServiceAccount";

    /// <summary>
    /// Gets or sets whether the token endpoint accepts the client credentials grant, which gives a
    /// super admin token to whoever holds the service account's secret. Defaults to true for
    /// backwards compatibility; a stack whose integrations do not use it turns it off.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
