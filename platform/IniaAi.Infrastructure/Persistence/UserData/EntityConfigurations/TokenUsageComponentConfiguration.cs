using IniaAi.Domain.Entities.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// EF Core configuration for the TokenUsageComponent entity.
/// This normalized table stores individual usage components (Input, Output, Cache, etc.)
/// and mirrors the ModelPricingComponent structure for direct cost calculation.
/// </summary>
public class TokenUsageComponentConfiguration : IEntityTypeConfiguration<TokenUsageComponent>
{
    private readonly DatabaseFacade _database;

    public TokenUsageComponentConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<TokenUsageComponent> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("TokenUsageComponents");

        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.Id)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // REQUIRED FIELDS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.TokenUsageId)
            .IsRequired()
            .HasMaxLength(36);

        _ = builder.Property(e => e.ComponentType)
            .HasConversion<int>()
            .IsRequired();

        _ = builder.Property(e => e.Units)
            .IsRequired();

        _ = builder.Property(e => e.Cost)
            .IsRequired()
            .HasColumnType(isPostgres ? "numeric(18,10)" : "decimal(18,10)");

        _ = builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        // ═══════════════════════════════════════════════════════════════
        // INDEXES
        // ═══════════════════════════════════════════════════════════════

        // Lookup by parent usage record
        _ = builder.HasIndex(e => e.TokenUsageId)
            .HasDatabaseName("IX_TokenUsageComponents_TokenUsageId");

        // Lookup by component type (for aggregation queries)
        _ = builder.HasIndex(e => new { e.TokenUsageId, e.ComponentType })
            .HasDatabaseName("IX_TokenUsageComponents_TokenUsageId_ComponentType")
            .IsUnique();

        // Component type analysis
        _ = builder.HasIndex(e => e.ComponentType)
            .HasDatabaseName("IX_TokenUsageComponents_ComponentType");
    }
}
