namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// The 429 backoff policy for a WebApiCall block: full jitter (a uniform draw between
/// <see cref="MinDelayMs"/> and <c>min(MaxDelayMs, BaseDelayMs * 2^retry)</c>) so blocks that
/// tripped the same key together do not retry together, bounded by a retry count and a total
/// backoff budget.
/// </summary>
public sealed record WebApiCallRateLimitSchedule(
    int MaxRetries,
    int MinDelayMs,
    int BaseDelayMs,
    int MaxDelayMs,
    int TotalWaitCeilingMs)
{
    private const int DEFAULT_MAX_RETRIES = 8;
    private const int DEFAULT_MIN_DELAY_MS = 500;
    private const int DEFAULT_BASE_DELAY_MS = 2000;
    private const int DEFAULT_MAX_DELAY_MS = 30000;
    private const int DEFAULT_TOTAL_WAIT_CEILING_MS = 120000;

    public static WebApiCallRateLimitSchedule Default { get; } = new(
        DEFAULT_MAX_RETRIES,
        DEFAULT_MIN_DELAY_MS,
        DEFAULT_BASE_DELAY_MS,
        DEFAULT_MAX_DELAY_MS,
        DEFAULT_TOTAL_WAIT_CEILING_MS);

    /// <summary>
    /// The jittered wait before retry number <paramref name="retryIndex"/> (zero-based).
    /// </summary>
    public int NextJitteredDelayMs(int retryIndex)
    {
        long exponential = (long)BaseDelayMs << Math.Clamp(retryIndex, 0, 30);
        int cap = (int)Math.Min(MaxDelayMs, exponential);
        int floor = Math.Min(MinDelayMs, cap);

        return Random.Shared.Next(floor, cap + 1);
    }
}
