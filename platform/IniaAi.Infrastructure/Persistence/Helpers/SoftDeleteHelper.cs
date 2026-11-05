using IniaAi.Domain.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Reflection;

namespace IniaAi.Infrastructure.Persistence.Helpers;

public static class SoftDeleteHelper
{
    public static void MarkEntitySoftDeleted<TEntity, TKey>(
        DbContext context,
        TKey id,
        string? deletedByUserId = null,
        Action<TEntity>? configureExtraProps = null)
        where TEntity : class, IAuditableEntity, new()
    {
        string? idString = id?.ToString();
        Type entityType = typeof(TEntity);

        // Try to find a tracked entity with matching Id
        EntityEntry<TEntity>? tracked = context.ChangeTracker
            .Entries<TEntity>()
            .FirstOrDefault(e =>
                e.State is not EntityState.Detached &&
                e.CurrentValues["Id"]?.ToString() == idString);

        if (tracked is not null)
        {
            TEntity entity = tracked.Entity;
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedByUserId = deletedByUserId;
            configureExtraProps?.Invoke(entity);
        }
        else
        {
            // Create a stub instance and set its ID via reflection
            var stub = new TEntity();

            PropertyInfo? idProp = entityType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (idProp is null || !idProp.CanWrite)
            {
                throw new InvalidOperationException($"{entityType.Name} must have a settable public 'Id' property to be soft-deleted by ID.");
            }

            idProp.SetValue(stub, idString);
            stub.IsDeleted = true;
            stub.DeletedAt = DateTime.UtcNow;
            stub.DeletedByUserId = deletedByUserId;
            configureExtraProps?.Invoke(stub);

            _ = context.Attach(stub);
            context.Entry(stub).Property(nameof(IAuditableEntity.IsDeleted)).IsModified = true;
            context.Entry(stub).Property(nameof(IAuditableEntity.DeletedAt)).IsModified = true;
            context.Entry(stub).Property(nameof(IAuditableEntity.DeletedByUserId)).IsModified = true;
        }
    }
}
