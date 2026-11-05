using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// A robust, thread-safe sliding window rate limiter for LLM input tokens,
/// designed to adjust estimates using real Anthropic headers,
/// handle concurrency correctly, and avoid Semaphore over-release.
/// </summary>
public class TokenSlidingWindowRateLimiter : IDisposable, IAsyncDisposable
{
    private int _tokenLimit;
    private readonly TimeSpan _window;

    private readonly Queue<(DateTime Timestamp, int Tokens)> _requests = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly ILogger<TokenSlidingWindowRateLimiter> _logger;

    private DateTime? _lastServerResetUtc;
    private int? _lastServerRemainingTokens;
    private DateTime _lastServerUpdateUtc = DateTime.MinValue;

    private bool _disposed;

    /// <summary>
    /// Creates a new sliding window limiter.
    /// </summary>
    /// <param name="tokenLimit">Initial max tokens per window.</param>
    /// <param name="window">Sliding window duration.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public TokenSlidingWindowRateLimiter(
        int tokenLimit,
        TimeSpan window,
        ILogger<TokenSlidingWindowRateLimiter> logger)
    {
        _tokenLimit = tokenLimit;
        _window = window;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Waits until enough estimated tokens are available.
    /// If server says budget exhausted, throws instead of blocking forever.
    /// Uses smarter backoff based on next expiration.
    /// </summary>
    /// <param name="estimatedTokens">Estimated input tokens for this call.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task WaitForPermissionAsync(int estimatedTokens, CancellationToken ct)
    {
        if (estimatedTokens > _tokenLimit)
        {
            _logger.LogWarning(
                "WaitForPermissionAsync: estimate {Estimated} exceeds window limit {Limit}.",
                estimatedTokens, _tokenLimit);
            estimatedTokens = _tokenLimit; // Cap to limit
        }

        DateTime startTime = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await _lock.WaitAsync(ct);
            bool permissionGranted = false;

            try
            {
                RemoveExpiredTokens();

                int used = _requests.Sum(x => x.Tokens);
                int limit = _tokenLimit;
                int remaining = limit - used;

                DateTime? serverResetUtc;
                int? serverRemaining;
                DateTime lastServerUpdate;

                lock (_stateLock)
                {
                    serverResetUtc = _lastServerResetUtc;
                    serverRemaining = _lastServerRemainingTokens;
                    lastServerUpdate = _lastServerUpdateUtc;
                }

                // Guard: clear stale server state if it's too old
                if (lastServerUpdate != DateTime.MinValue &&
                    DateTime.UtcNow - lastServerUpdate > TimeSpan.FromMinutes(5))
                {
                    _logger.LogWarning(
                        "Server limits stale for over 5 minutes — falling back to local window only.");
                    lock (_stateLock)
                    {
                        _lastServerRemainingTokens = null;
                        _lastServerResetUtc = null;
                        _lastServerUpdateUtc = DateTime.MinValue;
                    }
                }

                if (serverRemaining == 0 && serverResetUtc.HasValue)
                {
                    DateTime now = DateTime.UtcNow;
                    if (now < serverResetUtc.Value)
                    {
                        double waitSeconds = (serverResetUtc.Value - now).TotalSeconds;
                        _logger.LogWarning(
                            "Server budget exhausted — must wait ~{WaitSeconds:F1}s until reset at {ResetUtc}.",
                            waitSeconds, serverResetUtc.Value);

                        if (waitSeconds > 120)
                        {
                            throw new TokenBudgetExhaustedException(
                                $"Server token budget exhausted. Wait for {waitSeconds} seconds.");
                        }
                    }
                }

                if (used + estimatedTokens <= limit)
                {
                    _requests.Enqueue((DateTime.UtcNow, estimatedTokens));
                    _logger.LogDebug(
                        "TokenLimiter granted: used={Used} est={Est} remaining={Remaining}/{Limit}",
                        used, estimatedTokens, remaining, limit);
                    permissionGranted = true;
                    return;
                }

                _logger.LogDebug(
                    "TokenLimiter local throttling: used={Used} est={Est} remaining={Remaining}/{Limit} — retrying...",
                    used, estimatedTokens, remaining, limit);
            }
            finally
            {
                _ = _lock.Release();
            }

            if (!permissionGranted)
            {
                // Figure out when the next chunk expires
                var waitTime = TimeSpan.FromSeconds(10);

                await _lock.WaitAsync(ct);
                try
                {
                    if (_requests.TryPeek(out (DateTime Timestamp, int Tokens) next))
                    {
                        DateTime nextExpiry = next.Timestamp + _window;
                        TimeSpan untilExpiry = nextExpiry - DateTime.UtcNow;

                        if (untilExpiry > TimeSpan.Zero && untilExpiry < waitTime)
                        {
                            waitTime = untilExpiry;
                        }
                    }
                }
                finally
                {
                    _ = _lock.Release();
                }

                // Hard fail if total wait too long
                if (DateTime.UtcNow - startTime > TimeSpan.FromMinutes(5))
                {
                    throw new TimeoutException("WaitForPermissionAsync timed out after 5 minutes waiting for available tokens.");
                }

                await Task.Delay(waitTime, ct);
            }
        }
    }
    /// <summary>
    /// Updates the server limits and synchronizes usage.
    /// </summary>
    public void UpdateServerLimits(int remainingTokens, DateTime resetUtc, int? newTokenLimit = null)
    {
        lock (_stateLock)
        {
            _lastServerRemainingTokens = remainingTokens;
            _lastServerResetUtc = resetUtc;

            if (newTokenLimit.HasValue && newTokenLimit.Value != _tokenLimit)
            {
                _logger.LogDebug(
                    "Server limit changed: old={Old} -> new={New}.",
                    _tokenLimit, newTokenLimit.Value);
                _tokenLimit = newTokenLimit.Value;
            }

            int serverUsed = _tokenLimit - remainingTokens;
            SyncServerUsage(serverUsed);

            _logger.LogDebug(
                "Server limit updated: remaining={Remaining}, reset={ResetUtc}, synced used={Used}",
                remainingTokens, resetUtc, serverUsed);
        }
    }

    /// <summary>
    /// Hard-syncs sliding window with real server usage.
    /// </summary>
    private void SyncServerUsage(int actualServerUsed)
    {
        _requests.Clear();
        _requests.Enqueue((DateTime.UtcNow, actualServerUsed));

        _logger.LogDebug(
            "Sliding window synced to real server usage: used = {Used}",
            actualServerUsed);
    }

    /// <summary>
    /// Cleans up expired requests.
    /// </summary>
    private void RemoveExpiredTokens()
    {
        DateTime cutoff = DateTime.UtcNow - _window;
        int expired = 0;

        while (_requests.Count > 0 && _requests.Peek().Timestamp < cutoff)
        {
            _ = _requests.Dequeue();
            expired++;
        }

        if (expired > 0)
        {
            _logger.LogDebug("Removed {Expired} expired token chunks.", expired);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Dispose();
        _disposed = true;
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Thrown when the server reports token budget exhausted and local limiter must back off.
/// </summary>
public class TokenBudgetExhaustedException : Exception
{
    public TokenBudgetExhaustedException(string message) : base(message) { }
}
