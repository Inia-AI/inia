using IniaAi.Domain.Entities.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// EF Core configuration for the ModelPricingComponent entity.
/// This normalized table stores individual pricing components (Input, Output, Cache, etc.)
/// with support for different units and currencies.
/// </summary>
public class ModelPricingComponentConfiguration : IEntityTypeConfiguration<ModelPricingComponent>
{
    private readonly DatabaseFacade _database;

    public ModelPricingComponentConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<ModelPricingComponent> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("ModelPricingComponents");

        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.Id)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // REQUIRED FIELDS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.ModelPricingId)
            .IsRequired()
            .HasMaxLength(36);

        _ = builder.Property(e => e.ComponentType)
            .HasConversion<int>()
            .IsRequired();

        _ = builder.Property(e => e.Cost)
            .IsRequired()
            .HasColumnType(isPostgres ? "numeric(18,6)" : "decimal(18,6)");

        _ = builder.Property(e => e.Unit)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(Domain.Entities.TokenUsage.Enums.PricingUnit.PerMillion);

        _ = builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("USD");

        _ = builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        // ═══════════════════════════════════════════════════════════════
        // INDEXES
        // ═══════════════════════════════════════════════════════════════

        // Lookup by parent pricing record
        _ = builder.HasIndex(e => e.ModelPricingId)
            .HasDatabaseName("IX_ModelPricingComponents_ModelPricingId");

        // Lookup by component type
        _ = builder.HasIndex(e => new { e.ModelPricingId, e.ComponentType })
            .HasDatabaseName("IX_ModelPricingComponents_ModelPricingId_ComponentType")
            .IsUnique();

        // Currency filtering
        _ = builder.HasIndex(e => e.Currency)
            .HasDatabaseName("IX_ModelPricingComponents_Currency");
    }
}
