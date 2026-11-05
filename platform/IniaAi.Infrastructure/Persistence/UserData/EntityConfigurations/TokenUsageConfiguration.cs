using IniaAi.Domain.Entities.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// EF Core configuration for the TokenUsage entity.
/// </summary>
public class TokenUsageConfiguration : IEntityTypeConfiguration<Domain.Entities.TokenUsage.TokenUsage>
{
    private readonly DatabaseFacade _database;

    public TokenUsageConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<Domain.Entities.TokenUsage.TokenUsage> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;
        bool isInMemory = providerName?.Contains("inmemory") == true;

        _ = builder.ToTable("TokenUsages");

        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.Id)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // REQUIRED FIELDS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.OrganizationId)
            .IsRequired()
            .HasMaxLength(450);

        _ = builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        _ = builder.Property(e => e.Family)
            .IsRequired()
            .HasMaxLength(50);

        _ = builder.Property(e => e.ModelId)
            .IsRequired()
            .HasMaxLength(100);

        _ = builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        // ═══════════════════════════════════════════════════════════════
        // OPTIONAL FOREIGN KEYS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.UserId)
            .HasMaxLength(450);

        _ = builder.Property(e => e.WorkflowTypeId)
            .HasMaxLength(36);

        _ = builder.Property(e => e.WorkflowExecutionId)
            .HasMaxLength(450);

        _ = builder.Property(e => e.SectionId)
            .HasMaxLength(36);

        _ = builder.Property(e => e.BlockId)
            .HasMaxLength(36);

        _ = builder.Property(e => e.DocumentId)
            .HasMaxLength(450);

        _ = builder.Property(e => e.PageId)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // ENUM CONVERSIONS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.UsageType)
            .HasConversion<int>()
            .IsRequired();

        _ = builder.Property(e => e.ExecutionContext)
            .HasConversion<int>()
            .IsRequired();

        _ = builder.Property(e => e.BlockType)
            .HasConversion<int>();

        // ═══════════════════════════════════════════════════════════════
        // PRICING REFERENCE
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.ModelPricingId)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // DESCRIPTION & METADATA
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.Description)
            .HasMaxLength(500);

        _ = builder.Property(e => e.Metadata)
            .HasMaxLength(4000);

        // ═══════════════════════════════════════════════════════════════
        // INDEXES
        // ═══════════════════════════════════════════════════════════════

        // Primary access pattern: by organization and date
        _ = builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt })
            .HasDatabaseName("IX_TokenUsages_OrganizationId_CreatedAt");

        // By workflow execution (drill-down)
        _ = builder.HasIndex(e => e.WorkflowExecutionId)
            .HasDatabaseName("IX_TokenUsages_WorkflowExecutionId")
            .HasFilter(isPostgres
                ? "\"WorkflowExecutionId\" IS NOT NULL"
                : "[WorkflowExecutionId] IS NOT NULL");

        // By user
        _ = builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("IX_TokenUsages_UserId_CreatedAt")
            .HasFilter(isPostgres
                ? "\"UserId\" IS NOT NULL"
                : "[UserId] IS NOT NULL");

        // By usage type (LLM vs Embedding analysis)
        _ = builder.HasIndex(e => new { e.OrganizationId, e.UsageType, e.CreatedAt })
            .HasDatabaseName("IX_TokenUsages_OrganizationId_UsageType_CreatedAt");

        // By block type (pattern analysis)
        _ = builder.HasIndex(e => new { e.OrganizationId, e.BlockType, e.CreatedAt })
            .HasDatabaseName("IX_TokenUsages_OrganizationId_BlockType_CreatedAt")
            .HasFilter(isPostgres
                ? "\"BlockType\" IS NOT NULL"
                : "[BlockType] IS NOT NULL");

        // By execution context (production vs testing)
        _ = builder.HasIndex(e => new { e.OrganizationId, e.ExecutionContext, e.CreatedAt })
            .HasDatabaseName("IX_TokenUsages_OrganizationId_ExecutionContext_CreatedAt");

        // By document (document processing costs)
        _ = builder.HasIndex(e => e.DocumentId)
            .HasDatabaseName("IX_TokenUsages_DocumentId")
            .HasFilter(isPostgres
                ? "\"DocumentId\" IS NOT NULL"
                : "[DocumentId] IS NOT NULL");

        // Rows the usage-totals sweep has not added yet: what every usage read adds to the totals
        IndexBuilder<Domain.Entities.TokenUsage.TokenUsage> notInTotals = builder
            .HasIndex(e => new { e.OrganizationId, e.CreatedAt }, TokenUsageNotInTotalsIndex.NAME)
            .HasFilter(TokenUsageNotInTotalsIndex.Filter(isPostgres));
        _ = isPostgres
            ? NpgsqlIndexBuilderExtensions.IncludeProperties(notInTotals, [.. TokenUsageNotInTotalsIndex.IncludedColumns])
            : SqlServerIndexBuilderExtensions.IncludeProperties(notInTotals, [.. TokenUsageNotInTotalsIndex.IncludedColumns]);

        // ═══════════════════════════════════════════════════════════════
        // RELATIONSHIPS
        // ═══════════════════════════════════════════════════════════════

        // WorkflowType is a cross-context entity (lives in CoreDbContext),
        // so we ignore the navigation property and don't create a FK constraint.
        // We just store the WorkflowTypeId as a regular column.
        _ = builder.Ignore(e => e.WorkflowType);

        // Block is a cross-context reference - blocks in workflow templates
        // don't exist in the Blocks table (only persisted workflow blocks do).
        // We just store the BlockId as a regular column without FK constraint.
        _ = builder.Ignore(e => e.Block);

        // ModelPricing is a cross-context entity (lives in CoreDbContext),
        // so we ignore the navigation property and don't create a FK constraint.
        // We just store the ModelPricingId as a regular column.
        _ = builder.Ignore(e => e.ModelPricing);

        // For in-memory databases (testing), ignore navigation properties
        // to avoid entity discovery issues with cross-context entities
        if (isInMemory)
        {
            _ = builder.Ignore(e => e.Organization);
            _ = builder.Ignore(e => e.User);
            _ = builder.Ignore(e => e.WorkflowExecution);
            _ = builder.Ignore(e => e.Document);
        }
        else
        {
            _ = builder.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            _ = builder.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            _ = builder.HasOne(e => e.WorkflowExecution)
                .WithMany()
                .HasForeignKey(e => e.WorkflowExecutionId)
                .OnDelete(DeleteBehavior.SetNull);

            _ = builder.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.NoAction);
        }

        // ═══════════════════════════════════════════════════════════════
        // COMPONENTS RELATIONSHIP
        // ═══════════════════════════════════════════════════════════════

        _ = builder.HasMany(e => e.Components)
            .WithOne(c => c.TokenUsage)
            .HasForeignKey(c => c.TokenUsageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
