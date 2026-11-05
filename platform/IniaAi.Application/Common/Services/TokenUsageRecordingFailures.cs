namespace IniaAi.Application.Common.Services;

/// <summary>
/// The usage records this process failed to save since it started. A failed save is spend that
/// never reaches token usage or billing, and the caller does not wait for it, so the count is kept
/// here and the <c>Token usage recording</c> health check reports it on <c>/readiness</c>.
/// </summary>
public sealed class TokenUsageRecordingFailures
{
    private readonly Lock _lock = new();
    private int _count;
    private string? _lastFailure;
    private DateTime? _lastFailedAt;

    public static TokenUsageRecordingFailures Instance { get; } = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public (string Description, DateTime At)? Last
    {
        get
        {
            lock (_lock)
            {
                return _lastFailure is null || _lastFailedAt is null ? null : (_lastFailure, _lastFailedAt.Value);
            }
        }
    }

    public void Record(string description)
    {
        lock (_lock)
        {
            _count++;
            _lastFailure = description;
            _lastFailedAt = DateTime.UtcNow;
        }
    }
}
