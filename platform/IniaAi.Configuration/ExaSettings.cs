namespace IniaAi.Configuration;

public class ExaSettings
{
    public string ExaApiUrl { get; set; } = "https://api.exa.ai";

    /// <summary>
    /// Exa API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request timeout in minutes for Exa API calls.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of retry attempts for failed API requests.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
