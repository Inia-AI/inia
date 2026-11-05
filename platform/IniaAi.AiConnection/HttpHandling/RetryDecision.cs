using IniaAi.AiConnection.HttpHandling.Enums;

namespace IniaAi.AiConnection.HttpHandling;

/// <summary>
/// Represents the decision of whether to retry a request
/// </summary>
internal class RetryDecision
{
    /// <summary>
    /// Whether the request should be retried
    /// </summary>
    public bool ShouldRetry { get; set; }

    /// <summary>
    /// The type of retry being performed
    /// </summary>
    public RetryType RetryType { get; set; }

    /// <summary>
    /// Delay in seconds before retrying (if ShouldRetry is true)
    /// </summary>
    public double DelaySeconds { get; set; }

    /// <summary>
    /// Updated rate limit attempt count
    /// </summary>
    public int UpdatedRateLimitAttempts { get; set; }

    /// <summary>
    /// Updated unavailable attempt count
    /// </summary>
    public int UpdatedUnavailableAttempts { get; set; }
}
