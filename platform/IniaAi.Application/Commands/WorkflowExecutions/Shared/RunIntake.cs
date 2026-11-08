using ErrorOr;
using IniaAi.Application.Errors;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Whether this process starts new runs. A deploy pauses it before it replaces the process, so the
/// runs in flight can finish while no new one starts here; a new process starts open. A pause lasts
/// a set time and then lifts by itself, so a deploy that stops before it opens intake again does
/// not leave the process refusing runs.
/// </summary>
public sealed class RunIntake(TimeProvider? clock = null)
{
    public const string PAUSED_CODE = "RunIntake.Paused";

    public const string PAUSED_MESSAGE = "The platform is being updated. New reports can start again in a few minutes.";

    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private long _pausedUntilTicks;

    /// <summary>When the pause lifts, or null while intake is open.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            long until = Interlocked.Read(ref _pausedUntilTicks);
            return until > _clock.GetUtcNow().UtcTicks ? new DateTimeOffset(until, TimeSpan.Zero) : null;
        }
    }

    public bool IsPaused => PausedUntil is not null;

    public void Pause(TimeSpan lasting)
    {
        _ = Interlocked.Exchange(ref _pausedUntilTicks, (_clock.GetUtcNow() + lasting).UtcTicks);
    }

    public void Open()
    {
        _ = Interlocked.Exchange(ref _pausedUntilTicks, 0);
    }

    public static Error Refusal()
    {
        return ServiceUnavailableError.Of(PAUSED_CODE, PAUSED_MESSAGE, RetryAfter);
    }
}
