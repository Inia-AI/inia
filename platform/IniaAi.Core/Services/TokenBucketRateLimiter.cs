namespace IniaAi.Core.Services;

/// <summary>
/// A simple, thread-safe Token Bucket Rate Limiter for controlling the rate of operations.
/// </summary>
/// <remarks>
/// This implementation supports both burst capacity and smooth refill at a defined rate.
/// Use it to limit outgoing requests (e.g., LLM calls) to avoid exceeding provider quotas.
/// </remarks>
public class TokenBucketRateLimiter
{
    private readonly int _capacity;
    private readonly double _fillRatePerSecond;
    private double _tokens;
    private DateTime _lastRefillTimestamp;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenBucketRateLimiter"/> class.
    /// </summary>
    /// <param name="requestsPerInterval">
    /// The maximum number of allowed requests per time interval.
    /// This defines the bucket's capacity (burst size).
    /// </param>
    /// <param name="interval">
    /// The interval in which the defined number of requests is allowed.
    /// For example, use TimeSpan.FromMinutes(1) for requests per minute.
    /// </param>
    public TokenBucketRateLimiter(int requestsPerInterval, TimeSpan interval)
    {
        _capacity = requestsPerInterval;
        _fillRatePerSecond = requestsPerInterval / interval.TotalSeconds;
        _tokens = requestsPerInterval;
        _lastRefillTimestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Waits asynchronously until permission is granted to proceed with an operation.
    /// </summary>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> that cancels the wait operation if triggered.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when permission is granted.
    /// </returns>
    /// <remarks>
    /// If there are no available tokens, this method waits until new tokens are refilled.
    /// </remarks>
    public async Task WaitForPermissionAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await _lock.WaitAsync(ct);
            try
            {
                RefillTokens();

                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return; // permission granted
                }
            }
            finally
            {
                _ = _lock.Release();
            }

            // Not enough tokens, wait a bit before retrying
            await Task.Delay(100, ct);
        }
    }

    /// <summary>
    /// Recalculates and refills the token bucket based on elapsed time.
    /// </summary>
    /// <remarks>
    /// This method adds tokens to the bucket at the configured fill rate.
    /// The total tokens are capped at the bucket's capacity.
    /// </remarks>
    private void RefillTokens()
    {
        DateTime now = DateTime.UtcNow;
        double elapsedSeconds = (now - _lastRefillTimestamp).TotalSeconds;
        double refillAmount = elapsedSeconds * _fillRatePerSecond;

        if (refillAmount > 0)
        {
            _tokens = Math.Min(_tokens + refillAmount, _capacity);
            _lastRefillTimestamp = now;
        }
    }
}
