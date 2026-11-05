using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Persistence;

/// <summary>
/// Inside a rerun (<see cref="RerunWrites"/>), drops every added, changed or deleted row of a
/// <see cref="LiveDataAttribute"/> entity before the save, and logs what it dropped. Every other
/// row saves as usual, so the rerun's own execution, blocks, logs and usage are recorded. One place
/// covers every handler and block strategy, including ones written later.
/// </summary>
public sealed class RerunWriteInterceptor(ILogger<RerunWriteInterceptor> logger) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        DropLiveDataChanges(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        DropLiveDataChanges(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void DropLiveDataChanges(DbContext? context)
    {
        if (context is null || !RerunWrites.AreSuppressed)
        {
            return;
        }

        if (!context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        List<EntityEntry> dropped = [.. context.ChangeTracker.Entries()
            .Where(static entry => IsWrite(entry) && RerunWrites.IsLiveData(entry.Metadata.ClrType))];

        // A new row that hangs off a dropped new row would break its foreign key on insert, so it
        // goes too. A row that only points at an existing live row keeps it, which stays saved.
        HashSet<object> droppedEntities = new(
            dropped.Where(static entry => entry.State is EntityState.Added).Select(static entry => entry.Entity),
            ReferenceEqualityComparer.Instance);
        List<EntityEntry> dependents;
        do
        {
            dependents = [.. context.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added
                    && !droppedEntities.Contains(entry.Entity)
                    && entry.References.Any(reference => reference.CurrentValue is { } principal && droppedEntities.Contains(principal)))];

            foreach (EntityEntry dependent in dependents)
            {
                _ = droppedEntities.Add(dependent.Entity);
                dropped.Add(dependent);
            }
        }
        while (dependents.Count > 0);

        foreach (EntityEntry entry in dropped)
        {
            entry.State = EntityState.Detached;
        }

        foreach (IGrouping<string, EntityEntry> group in dropped.GroupBy(static entry => entry.Metadata.ClrType.Name))
        {
            logger.LogInformation(
                "Rerun: did not save {Count} change(s) to {EntityType}; a rerun leaves live data unchanged",
                group.Count(),
                group.Key);
        }
    }

    private static bool IsWrite(EntityEntry entry)
    {
        return entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
    }
}
