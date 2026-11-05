using IniaAi.Domain.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace IniaAi.Infrastructure.Persistence.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplySoftDeleteFilter(this ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;

            // Skip known problematic type(s)
            if (clrType == typeof(Common.Content.BinaryContent))
            {
                continue;
            }

            if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
                MemberExpression prop = Expression.Property(parameter, nameof(IAuditableEntity.IsDeleted));
                LambdaExpression filter = Expression.Lambda(Expression.Equal(prop, Expression.Constant(false)), parameter);

                _ = modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public static void UseSingularTableNames(this ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type? clrType = entityType.ClrType;

            // Skip known problematic type(s)
            if (clrType == typeof(Common.Content.BinaryContent))
            {
                continue;
            }
            // Skip shared-type entities (no CLR type or explicitly marked shared)
            if (entityType.IsOwned() ||
                entityType.IsKeyless ||
                entityType.ClrType is null ||
                entityType.ClrType == typeof(Dictionary<string, object>) ||
                entityType.ClrType.IsAbstract ||
                entityType.HasSharedClrType)
            {
                continue;
            }

            // Only configure root entity types
            _ = modelBuilder.Entity(entityType.ClrType).ToTable(entityType.ClrType.Name);
        }
    }

    public static void UseUtcDateTimeColumnsIfPostgres(this ModelBuilder modelBuilder, DbContext context)
    {
        if (!context.Database.IsNpgsql())
        {
            return;
        }

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;

            // Skip known problematic type(s)
            if (clrType == typeof(Common.Content.BinaryContent))
            {
                continue;
            }

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetColumnType("timestamp without time zone");
                }
            }
        }
    }
}
