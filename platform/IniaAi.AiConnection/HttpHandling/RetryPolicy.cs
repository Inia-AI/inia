using IniaAi.Configuration.LLMProviders;

namespace IniaAi.AiConnection.HttpHandling;

internal class RetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts for rate limit (429) responses
    /// </summary>
    public int MaxRateLimitRetries { get; set; } = 5;

    /// <summary>
    /// Maximum number of retry attempts for service unavailability (503, 529) responses or exceptions
    /// </summary>
    public int MaxUnavailableRetries { get; set; } = 3;

    /// <summary>
    /// Default delay in seconds between retries for service unavailability
    /// </summary>
    public int DefaultUnavailableRetryDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Maximum delay in seconds between retries
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 60;

    /// <summary>
    /// HTTP header to check for retry-after information
    /// </summary>
    public string RetryAfterHeader { get; set; } = "Retry-After";

    /// <summary>
    /// Whether to enable TCP keep-alive for the connection
    /// </summary>
    public bool EnableTcpKeepAlive { get; set; } = false;

    /// <summary>
    /// Time in milliseconds between the last packet sent and the first keep-alive probe
    /// </summary>
    public int TcpKeepAliveTime { get; set; } = 30000; // 30 seconds

    /// <summary>
    /// Time in milliseconds between keep-alive probes
    /// </summary>
    public int TcpKeepAliveInterval { get; set; } = 1000; // 1 second

    /// <summary>
    /// HTTP request timeout in minutes
    /// </summary>
    public int HttpTimeoutMinutes { get; set; } = 10; // 10 minutes

    /// <summary>
    /// Creates a RetryPolicy with default values
    /// </summary>
    public RetryPolicy()
    {
    }

    /// <summary>
    /// Creates a RetryPolicy using values from AnthropicSettings
    /// </summary>
    /// <param name="anthropicSettings">The Anthropic settings to use for retry configuration</param>
    public RetryPolicy(AnthropicSettings anthropicSettings)
    {
        MaxRateLimitRetries = anthropicSettings.MaxRateLimitRetryAttempts;
        MaxUnavailableRetries = anthropicSettings.MaxServiceUnavailableRetryAttempts;
        HttpTimeoutMinutes = anthropicSettings.HttpTimeoutMinutes;
    }

    /// <summary>
    /// Creates a RetryPolicy using values from CohereSettings
    /// </summary>
    /// <param name="cohereSettings">The Cohere settings to use for retry configuration</param>
    public RetryPolicy(CohereSettings cohereSettings)
    {
        MaxRateLimitRetries = cohereSettings.MaxRateLimitRetryAttempts;
        MaxUnavailableRetries = cohereSettings.MaxServiceUnavailableRetryAttempts;
        // CohereSettings doesn't have HttpTimeoutMinutes - using default (10 minutes)
        // Embeddings operations are typically faster than chat completions
    }
}
