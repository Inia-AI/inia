using IniaAi.Domain.Entities.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// EF Core configuration for the ModelPricing entity.
/// ModelPricing is a Core entity as it represents global pricing data shared across all tenants.
/// Pricing components are stored in a normalized child table (ModelPricingComponents).
/// </summary>
public class ModelPricingConfiguration : IEntityTypeConfiguration<ModelPricing>
{
    private readonly DatabaseFacade _database;

    public ModelPricingConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<ModelPricing> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("ModelPricings");

        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.Id)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // REQUIRED FIELDS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        _ = builder.Property(e => e.ModelId)
            .IsRequired()
            .HasMaxLength(100);

        _ = builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        _ = builder.Property(e => e.PricingType)
            .HasConversion<int>()
            .IsRequired();

        _ = builder.Property(e => e.EffectiveFrom)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        _ = builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        _ = builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        // ═══════════════════════════════════════════════════════════════
        // OPTIONAL FIELDS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.Family)
            .HasMaxLength(50);

        _ = builder.Property(e => e.ModelAlias)
            .HasMaxLength(100);

        // ═══════════════════════════════════════════════════════════════
        // RELATIONSHIPS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.HasMany(e => e.Components)
            .WithOne(c => c.ModelPricing)
            .HasForeignKey(c => c.ModelPricingId)
            .OnDelete(DeleteBehavior.Cascade);

        // ═══════════════════════════════════════════════════════════════
        // INDEXES
        // ═══════════════════════════════════════════════════════════════

        // Lookup by provider and model
        _ = builder.HasIndex(e => new { e.Provider, e.ModelId, e.IsActive })
            .HasDatabaseName("IX_ModelPricings_Provider_ModelId_IsActive");

        // Lookup by model alias
        _ = builder.HasIndex(e => new { e.ModelAlias, e.IsActive })
            .HasDatabaseName("IX_ModelPricings_ModelAlias_IsActive")
            .HasFilter(isPostgres
                ? "\"ModelAlias\" IS NOT NULL"
                : "[ModelAlias] IS NOT NULL");

        // Active pricing lookup
        _ = builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_ModelPricings_IsActive");

        // Lookup by family (for filtering)
        _ = builder.HasIndex(e => new { e.Family, e.IsActive })
            .HasDatabaseName("IX_ModelPricings_Family_IsActive")
            .HasFilter(isPostgres
                ? "\"Family\" IS NOT NULL"
                : "[Family] IS NOT NULL");

        // Unique constraint: only one active pricing per provider/model
        _ = builder.HasIndex(e => new { e.Provider, e.ModelId, e.EffectiveFrom })
            .HasDatabaseName("IX_ModelPricings_Provider_ModelId_EffectiveFrom")
            .IsUnique();
    }
}
