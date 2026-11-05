using IniaAi.Application.Common.DataManagement;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.DataManagement;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Jobs;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Background service that periodically checks whether any organization is due for
/// soft-delete cleanup based on its configured day-of-week and hour (UTC).
/// Checks hourly to see if it's the right time to run.
/// </summary>
public class SoftDeleteCleanupBackgroundService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SoftDeleteCleanupBackgroundService> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Constructor with dependencies injected via DI. Uses IServiceScopeFactory to create scopes for each cleanup cycle.
    /// </summary>
    /// <param name="scopeFactory">IServiceScopeFactory for creating scopes to resolve scoped services during cleanup cycles</param>
    /// <param name="logger">ILogger for logging information and errors</param>
    /// <param name="clock">The clock that decides whether an organization is due; the system clock when null.</param>
    public SoftDeleteCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SoftDeleteCleanupBackgroundService> logger,
        TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation("SoftDeleteCleanup: Background service starting, waiting {Delay} before first run",
            StartupDelay);
    }

    /// <inheritdoc/>
    protected override void OnStopped()
    {
        _logger.LogInformation("SoftDeleteCleanup: Background service stopped");
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "SoftDeleteCleanup: Unhandled error in cleanup cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        ISoftDeleteCleanupService cleanupService = scope.ServiceProvider.GetRequiredService<ISoftDeleteCleanupService>();
        IWorkflowTypeRetirement workflowTypeRetirement = scope.ServiceProvider.GetRequiredService<IWorkflowTypeRetirement>();
        IDeletedRecordRetention deletedRecordRetention = scope.ServiceProvider.GetRequiredService<IDeletedRecordRetention>();

        List<Organization> organizations = await unitOfWork.Repository<Organization>()
            .GetAll()
            .Where(o => o.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        _logger.LogDebug("SoftDeleteCleanup: Checking {Count} organizations", organizations.Count);

        DateTime now = _clock.GetUtcNow().UtcDateTime;

        foreach (Organization org in organizations)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                OrganizationCleanupSchedule settings = await OrganizationCleanupSchedule.ForAsync(unitOfWork, deletedRecordRetention, org.Id, ct);

                if (!settings.IsEnabled)
                {
                    continue;
                }

                if (!IsDueForCleanup(now, settings.CleanupDayOfWeek, settings.CleanupHourUtc, settings.LastCleanupAt))
                {
                    continue;
                }

                _logger.LogInformation("SoftDeleteCleanup: Org {OrgId} — starting cleanup (scheduled {Day} {Hour}:00 UTC)",
                    org.Id, settings.CleanupDayOfWeek, settings.CleanupHourUtc);

                CleanupResult result = await cleanupService.CleanupAsync(org.Id, settings.RetentionDays, ct, settings.SharedRetentionDays);
                RetiredWorkflowTypePurgeResult retiredTypes = await workflowTypeRetirement.PurgeRetiredAsync(org.Id, settings.SharedRetentionDays, ct);

                if (result.Errors.Count == 0 && retiredTypes.Errors.Count == 0)
                {
                    await UpdateLastCleanupAtAsync(unitOfWork, org.Id, ct);
                }

                if (retiredTypes.PurgedWorkflowTypeIds.Count > 0 || retiredTypes.Errors.Count > 0)
                {
                    _logger.LogInformation(
                        "SoftDeleteCleanup: Org {OrgId} — purged {Purged} retired workflow type(s), {Errors} errors",
                        org.Id, retiredTypes.PurgedWorkflowTypeIds.Count, retiredTypes.Errors.Count);
                }

                if (result.TotalDeleted > 0 || result.FilesDeleted > 0)
                {
                    _logger.LogInformation(
                        "SoftDeleteCleanup: Org {OrgId} — finished: {Total} records, {Files} files, {Errors} errors",
                        org.Id, result.TotalDeleted, result.FilesDeleted, result.Errors.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SoftDeleteCleanup: Failed for org {OrgId}, continuing with next", org.Id);
            }
        }
    }

    /// <summary>
    /// Checks if cleanup should run now: correct day of week, correct hour, and hasn't already run today.
    /// </summary>
    private static bool IsDueForCleanup(DateTime now, DayOfWeek scheduledDay, int scheduledHour, DateTime? lastCleanupAt)
    {
        // Not the right day
        if (now.DayOfWeek != scheduledDay)
        {
            return false;
        }

        // Not the right hour
        if (now.Hour != scheduledHour)
        {
            return false;
        }

        // Already ran recently (within the last 23 hours to avoid double-runs
        // while still allowing the hourly check to catch it)
        if (lastCleanupAt.HasValue && (now - lastCleanupAt.Value).TotalHours < 23)
        {
            return false;
        }

        return true;
    }

    private static async Task UpdateLastCleanupAtAsync(
        IUserDataUnitOfWork unitOfWork, string organizationId, CancellationToken ct)
    {
        // Use IgnoreQueryFilters to find the record even if query filters would hide it,
        // preventing a duplicate insert that would violate the unique index
        OrganizationCleanupSettings? settings = await unitOfWork.Repository<OrganizationCleanupSettings>()
            .GetAll()
            .IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (settings is not null)
        {
            settings.LastCleanupAt = DateTime.UtcNow;
        }
        else
        {
            // Create a minimal record just to track LastCleanupAt
            // Other settings will fall through to global/defaults
            await unitOfWork.Repository<OrganizationCleanupSettings>().AddAsync(
                new OrganizationCleanupSettings
                {
                    OrganizationId = organizationId,
                    LastCleanupAt = DateTime.UtcNow,
                });
        }

        _ = await unitOfWork.CompleteAsync(ct);
    }
}
