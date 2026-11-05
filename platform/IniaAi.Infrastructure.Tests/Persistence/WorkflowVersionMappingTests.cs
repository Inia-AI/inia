using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class WorkflowVersionMappingTests
{
    // ParentVersion is a nullable self-reference. SQL Server rejects SET NULL or CASCADE on a
    // self-referencing FK (error 1785), so NO ACTION is the only valid delete behavior. That is
    // safe because version history is append-only — versions are never hard-deleted, so the FK is
    // never exercised by a parent delete. This pins the model config identically across providers.
    [Fact]
    public void ParentVersion_ForeignKey_IsOptionalAndNoActionOnDelete()
    {
        using CoreDbContext context = CreateModelContext();

        IForeignKey foreignKey = context.Model
            .FindEntityType(typeof(WorkflowVersion))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(WorkflowVersion));

        Assert.False(foreignKey.IsRequired, "Parent pointer must stay optional.");
        Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void WorkflowVersion_HasLookupAndContentHashIndexes()
    {
        using CoreDbContext context = CreateModelContext();

        List<string> indexes =
        [
            .. context.Model
                .FindEntityType(typeof(WorkflowVersion))!
                .GetIndexes()
                .Select(index => string.Join(",", index.Properties.Select(p => p.Name)))
        ];

        Assert.Contains(
            $"{nameof(WorkflowVersion.WorkflowTypeId)},{nameof(WorkflowVersion.CreatedAt)}",
            indexes);
        Assert.Contains(nameof(WorkflowVersion.ContentHash), indexes);
    }

    [Fact]
    public void WorkflowJsonCompressed_MapsToUnboundedColumn()
    {
        using CoreDbContext context = CreateModelContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(WorkflowVersion))!;

        IProperty compressed = entityType.FindProperty(nameof(WorkflowVersion.WorkflowJsonCompressed))!;
        Assert.Null(compressed.GetMaxLength());

        Assert.Null(entityType.FindProperty(nameof(WorkflowVersion.WorkflowJson)));
    }

    [Fact]
    public void WorkflowJsonCompressed_IsNullable_SoRetentionCanTrimThePayload()
    {
        using CoreDbContext context = CreateModelContext();

        IProperty compressed = context.Model
            .FindEntityType(typeof(WorkflowVersion))!
            .FindProperty(nameof(WorkflowVersion.WorkflowJsonCompressed))!;

        Assert.True(compressed.IsNullable);
    }

    [Fact]
    public void PayloadTrimmedAt_IsMappedAndNullable()
    {
        using CoreDbContext context = CreateModelContext();

        IProperty trimmedAt = context.Model
            .FindEntityType(typeof(WorkflowVersion))!
            .FindProperty(nameof(WorkflowVersion.PayloadTrimmedAt))!;

        Assert.NotNull(trimmedAt);
        Assert.True(trimmedAt.IsNullable);
    }

    private static CoreDbContext CreateModelContext()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new CoreDbContext(options);
    }
}
