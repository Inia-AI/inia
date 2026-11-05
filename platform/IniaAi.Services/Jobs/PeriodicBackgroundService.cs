using Microsoft.Extensions.Hosting;

namespace IniaAi.Services.Jobs;

/// <summary>
/// The loop every periodic sweep shares: a check that may keep it off this host, a start delay, then
/// one run at once and one on every tick of the interval until the host stops. A run that fails is
/// reported and the loop goes on; only the host stopping ends it, and a service that started always
/// hears that it stopped. Each service writes its own log lines.
/// </summary>
public abstract class PeriodicBackgroundService : BackgroundService
{
    /// <summary>
    /// The time between runs. A timer refuses zero, so a service clamps its setting.
    /// </summary>
    protected abstract TimeSpan Interval { get; }

    protected virtual TimeSpan StartupDelay => TimeSpan.Zero;

    /// <summary>
    /// Whether this host runs the sweep at all. A service that says no logs why.
    /// </summary>
    protected virtual bool ShouldStart()
    {
        return true;
    }

    protected virtual void OnStarting()
    {
    }

    protected virtual void OnStopped()
    {
    }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected abstract void OnRunFailed(Exception exception);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ShouldStart())
        {
            return;
        }

        OnStarting();

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);

            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    OnRunFailed(exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        OnStopped();
    }
}
