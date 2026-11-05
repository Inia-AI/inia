namespace IniaAi.Configuration.Security;

/// <summary>
/// Host names a reverse proxy in front of the app may name in X-Forwarded-Host.
/// </summary>
public class ForwardedHostSettings
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Gets or sets the host names, without a port, that the app accepts from X-Forwarded-Host
    /// and then treats as its own public host, e.g. ["admin.teleport.example.com"].
    /// If empty, X-Forwarded-Host is ignored.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];
}
