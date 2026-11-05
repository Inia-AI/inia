using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IniaAi.Infrastructure.Persistence;

/// <summary>
/// Records who deleted a row: on every save, a soft-deleted row that names no deleting user gets
/// the acting user. One place covers the repositories and every handler that soft-deletes by hand.
/// </summary>
public sealed class SoftDeleteActorInterceptor(IActingUser actingUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampDeletions(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StampDeletions(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // Change detection is off for the scan: SaveChanges detects changes itself afterwards, and the
    // Core context has already detected them and switched detection off. A row deleted since it was
    // read is found by its original value; a row attached with Update() or by the soft-delete stub
    // has no older value, so its delete flag marked modified finds it. The stamp goes through the
    // change tracker, so it is saved in every case.
    private void StampDeletions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        bool autoDetectChanges = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            string? userId = null;
            foreach (EntityEntry<IAuditableEntity> entry in context.ChangeTracker.Entries<IAuditableEntity>())
            {
                if (!IsNewlyDeleted(entry) || entry.Entity.DeletedByUserId is not null)
                {
                    continue;
                }

                userId ??= actingUser.UserId;
                if (string.IsNullOrEmpty(userId))
                {
                    return;
                }

                entry.Property(entity => entity.DeletedByUserId).CurrentValue = userId;
            }
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    private static bool IsNewlyDeleted(EntityEntry<IAuditableEntity> entry)
    {
        if (entry.State is not (EntityState.Modified or EntityState.Unchanged) || !entry.Entity.IsDeleted)
        {
            return false;
        }

        PropertyEntry<IAuditableEntity, bool> isDeleted = entry.Property(entity => entity.IsDeleted);
        return isDeleted.IsModified || !isDeleted.OriginalValue;
    }
}

/// <summary>
/// The acting user where no request or session supplies one: a background job, a seeder, a test.
/// </summary>
public sealed class NoActingUser : IActingUser
{
    public string? UserId => null;
}
