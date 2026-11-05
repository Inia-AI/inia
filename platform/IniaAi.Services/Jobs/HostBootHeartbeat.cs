using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Domain.Entities.WorkflowExecutions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace IniaAi.Services.Jobs;

/// <summary>
/// One heartbeat of this process start: moves its <see cref="HostBoot"/> row's last-seen time to now,
/// and writes the row on the first beat.
/// </summary>
public class HostBootHeartbeat
{
    private readonly IUserDataDbContext _context;
    private readonly HostBootIdentity _hostBoot;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public HostBootHeartbeat(
        IUserDataDbContext context,
        HostBootIdentity hostBoot,
        IHostEnvironment environment,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _hostBoot = hostBoot;
        _environment = environment;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task BeatAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            await WriteLastSeenAsync(now.UtcDateTime, cancellationToken);
        }
        catch
        {
            _hostBoot.HeartbeatFailed();
            throw;
        }

        _hostBoot.HeartbeatSucceeded(now);
    }

    private async Task WriteLastSeenAsync(DateTime now, CancellationToken cancellationToken)
    {
        int moved = await _context.Set<HostBoot>()
            .Where(boot => boot.Id == _hostBoot.Id)
            .ExecuteUpdateAsync(boot => boot.SetProperty(b => b.LastSeenAt, now), cancellationToken);
        if (moved > 0)
        {
            return;
        }

        _ = _context.Add(new HostBoot
        {
            Id = _hostBoot.Id,
            Application = _environment.ApplicationName,
            Machine = Environment.MachineName,
            StartedAt = now,
            LastSeenAt = now,
        });
        _ = await _context.SaveChangesAsync(cancellationToken);
    }
}
