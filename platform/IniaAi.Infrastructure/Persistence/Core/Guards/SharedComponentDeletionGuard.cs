using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.SharedComponents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IniaAi.Infrastructure.Persistence.Core.Guards;

/// <summary>
/// Refuses hard and soft deletion of shared-component registry rows: retiring (State = Retired) is
/// the only end-of-life transition. A soft-deleted SharedComponent vanishes from head lookups
/// through the global IsDeleted query filter while live template copies still carry its id, and
/// SharedComponentVersion rows are append-only lineage that consumer version hashes point into.
/// Bulk operations that bypass the change tracker (ExecuteDelete, raw SQL) are not covered; no
/// code path uses them for these tables.
/// </summary>
public static class SharedComponentDeletionGuard
{
    public static void Validate(ChangeTracker changeTracker)
    {
        foreach (EntityEntry entry in changeTracker.Entries())
        {
            if (entry.Entity is not (SharedComponent or SharedComponentVersion))
            {
                continue;
            }

            bool hardDeleted = entry.State == EntityState.Deleted;
            bool softDeleted = entry.State == EntityState.Modified
                && entry.Entity is IAuditableEntity { IsDeleted: true };
            if (hardDeleted || softDeleted)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} rows must never be deleted; retire the component "
                    + "(State = Retired) instead. Deleting registry rows orphans live template links "
                    + "and breaks version lineage.");
            }
        }
    }
}
