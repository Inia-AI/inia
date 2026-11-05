using IniaAi.Application.Common.DataManagement;
using IniaAi.Domain.Entities.DataManagement;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// When and how an organization's soft-deleted records are purged: its own explicit settings, else
/// the global row, else the defaults (Sunday 02:00 UTC, 30 days). Without explicit settings, the
/// retention is the one <see cref="IDeletedRecordRetention"/> gives the organization, when it gives one.
/// </summary>
/// <param name="RetentionDays">Days a soft-deleted record stays before it is purged.</param>
/// <param name="CleanupDayOfWeek">The day the sweep runs.</param>
/// <param name="CleanupHourUtc">The hour, UTC, the sweep runs.</param>
/// <param name="IsEnabled">Whether the organization is swept at all.</param>
/// <param name="LastCleanupAt">When the organization was last swept, from its tracking row.</param>
/// <param name="SharedRetentionDays">
/// The global or default retention, for what the sweep purges beyond the organization's own rows:
/// counterparty types and blocks no organization owns, and retired workflow types. An organization's
/// own retention never shortens them.
/// </param>
public sealed record OrganizationCleanupSchedule(
    int RetentionDays,
    DayOfWeek CleanupDayOfWeek,
    int CleanupHourUtc,
    bool IsEnabled,
    DateTime? LastCleanupAt,
    int SharedRetentionDays)
{
    private const int DEFAULT_RETENTION_DAYS = 30;
    private const DayOfWeek DEFAULT_CLEANUP_DAY = DayOfWeek.Sunday;
    private const int DEFAULT_CLEANUP_HOUR_UTC = 2;

    /// <summary>The organization's schedule, read from its settings and the global settings.</summary>
    public static async Task<OrganizationCleanupSchedule> ForAsync(
        IUserDataUnitOfWork unitOfWork,
        IDeletedRecordRetention deletedRecordRetention,
        string organizationId,
        CancellationToken cancellationToken)
    {
        OrganizationCleanupSettings? own = await unitOfWork.Repository<OrganizationCleanupSettings>()
            .GetAll()
            .IgnoreQueryFilters()
            .Where(settings => settings.OrganizationId == organizationId && !settings.IsDeleted)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        DateTime? lastCleanupAt = own?.LastCleanupAt;

        OrganizationCleanupSettings? global = await unitOfWork.Repository<OrganizationCleanupSettings>()
            .GetAll()
            .IgnoreQueryFilters()
            .Where(settings => settings.OrganizationId == null && !settings.IsDeleted)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        int sharedRetentionDays = global?.RetentionDays ?? DEFAULT_RETENTION_DAYS;

        if (own is { HasExplicitSettings: true })
        {
            return new OrganizationCleanupSchedule(own.RetentionDays, own.CleanupDayOfWeek, own.CleanupHourUtc, own.IsEnabled, lastCleanupAt, sharedRetentionDays);
        }

        var schedule = new OrganizationCleanupSchedule(
            sharedRetentionDays,
            global?.CleanupDayOfWeek ?? DEFAULT_CLEANUP_DAY,
            global?.CleanupHourUtc ?? DEFAULT_CLEANUP_HOUR_UTC,
            global?.IsEnabled ?? true,
            lastCleanupAt,
            sharedRetentionDays);

        return await deletedRecordRetention.RetentionDaysAsync(organizationId, cancellationToken) is { } retentionDays
            ? schedule with { RetentionDays = retentionDays }
            : schedule;
    }
}
