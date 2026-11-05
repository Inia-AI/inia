using IniaAi.Domain.Entities.Workflows.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the BlockTemplate entity.
/// </summary>
public class BlockTemplateConfiguration : IEntityTypeConfiguration<BlockTemplate>
{
    private readonly DatabaseFacade _database;

    public BlockTemplateConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<BlockTemplate> builder)
    {
        // Detect provider for syntax-specific constraints
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgreSql = providerName?.Contains("npgsql") == true;

        // Provider-specific CHECK constraint syntax
        string checkConstraintSql = isPostgreSql
            ? "(\"IsSystemTemplate\" = false) OR (\"IsSystemTemplate\" = true AND \"IsPublicTemplate\" = true AND \"OrganizationId\" IS NULL)"
            : "([IsSystemTemplate] = 0) OR ([IsSystemTemplate] = 1 AND [IsPublicTemplate] = 1 AND [OrganizationId] IS NULL)";

        _ = builder.ToTable("BlockTemplate", t =>
        {
            _ = t.HasCheckConstraint("CK_BlockTemplate_SystemTemplate", checkConstraintSql);
        });

        _ = builder.HasKey(x => x.Id);

        _ = builder.HasOne(x => x.Block)
               .WithOne()
               .HasForeignKey<BlockTemplate>(x => x.Id)
               .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Property(x => x.TemplateCode)
               .HasMaxLength(100)
               .IsRequired();

        _ = builder.HasIndex(x => x.TemplateCode)
               .IsUnique();

        _ = builder.Property(x => x.BlockType)
               .HasConversion<int>()
               .IsRequired();

        _ = builder.Property(x => x.OrganizationId)
               .HasMaxLength(450)
               .IsRequired(false);

        _ = builder.Property(x => x.TemplateScope)
               .HasConversion<int>()
               .IsRequired();

        _ = builder.Property(x => x.CategoryId)
               .IsRequired();

        _ = builder.Property(x => x.WorkflowTypeId)
               .HasMaxLength(450)
               .IsRequired(false);

        _ = builder.Property(x => x.DocumentCategoryId)
               .IsRequired(false);

        _ = builder.Property(x => x.ParentTemplateId)
               .HasMaxLength(450)
               .IsRequired(false);

        _ = builder.Property(x => x.TemplateVersion)
               .HasDefaultValue(1)
               .IsRequired();

        _ = builder.Property(x => x.TemplateUsageCount)
               .HasDefaultValue(0)
               .IsRequired();

        _ = builder.Property(x => x.IsSystemTemplate)
               .HasDefaultValue(false)
               .IsRequired();

        _ = builder.Property(x => x.IsPublicTemplate)
               .HasDefaultValue(false)
               .IsRequired();

        _ = builder.Property(x => x.CreatedByUserId)
               .HasMaxLength(450)
               .IsRequired();

        _ = builder.Property(x => x.CreatedAt)
               .HasDefaultValueSql(isPostgreSql ? "now()" : "GETUTCDATE()")
               .IsRequired();

        _ = builder.Property(x => x.UpdatedAt)
               .IsRequired(false);

        _ = builder.Property(x => x.UpdatedByUserId)
               .HasMaxLength(450)
               .IsRequired(false);

        _ = builder.HasOne(x => x.TemplateCategory)
               .WithMany()
               .HasForeignKey(x => x.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        _ = builder.HasOne(x => x.DocumentCategory)
               .WithMany()
               .HasForeignKey(x => x.DocumentCategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        // Self-referential relationship for parent template
        _ = builder.HasOne(x => x.ParentTemplate)
               .WithMany()
               .HasForeignKey(x => x.ParentTemplateId)
               .OnDelete(DeleteBehavior.Restrict);

        _ = builder.HasIndex(x => x.CategoryId);
        _ = builder.HasIndex(x => x.TemplateScope);

        _ = builder.HasIndex(x => x.DocumentCategoryId);
        _ = builder.HasIndex(x => x.WorkflowTypeId);
        _ = builder.HasIndex(x => x.ParentTemplateId);

        _ = builder.HasIndex(x => x.CreatedByUserId);
        _ = builder.HasIndex(x => x.CreatedAt)
               .IsDescending();

        IndexBuilder<BlockTemplate> indexBuilder = builder.HasIndex(x => new
        {
            x.TemplateScope,
            x.BlockType,
            x.OrganizationId,
            x.IsPublicTemplate,
            x.CategoryId
        })
        .HasDatabaseName("IX_BlockTemplate_Search");

        _ = SqlServerIndexBuilderExtensions.IncludeProperties(
            indexBuilder,
            x => new
            {
                x.TemplateUsageCount,
                x.WorkflowTypeId,
                x.DocumentCategoryId,
                x.CreatedByUserId,
                x.CreatedAt
            });
    }
}
