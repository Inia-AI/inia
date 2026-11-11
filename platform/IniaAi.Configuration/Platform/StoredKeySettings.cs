namespace IniaAi.Configuration.Platform;

public class StoredKeySettings
{
    /// <summary>
    /// The deployment's own value for a stored key name, such as <c>PerplexityApiKey</c>, used
    /// when the run's organization has no stored key of that name. Empty by default, so each
    /// organization uses only its own keys. The values are secrets: set them as secret settings,
    /// never in a committed file.
    /// </summary>
    public Dictionary<string, string> DeploymentKeys { get; set; } = [];

    /// <summary>The deployment's value for <paramref name="name"/>, or null when it sets none.</summary>
    public string? DeploymentValue(string name)
    {
        // Scanned rather than looked up so a dictionary the configuration binder rebuilt with
        // the default (case-sensitive) comparer still matches a name written in another case.
        foreach (KeyValuePair<string, string> entry in DeploymentKeys)
        {
            if (entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase) && entry.Value is { Length: > 0 })
            {
                return entry.Value;
            }
        }

        return null;
    }
}
