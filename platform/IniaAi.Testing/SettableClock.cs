namespace IniaAi.Testing;

/// <summary>
/// A clock a test sets or moves by hand, so code that waits on time (a lease, a backoff, an expiry,
/// another day) is driven without waiting. Timers still run on real time.
/// </summary>
public sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public DateTime UtcNow => Now.UtcDateTime;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }

    public void MoveTo(DateTime utcNow)
    {
        Now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    public void Advance(TimeSpan by)
    {
        Now += by;
    }
}
