using IniaAi.Domain.Entities.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// EF Core configuration for the OrganizationModelSettings entity.
/// </summary>
public class OrganizationModelSettingsConfiguration : IEntityTypeConfiguration<OrganizationModelSettings>
{
    private readonly DatabaseFacade _database;

    public OrganizationModelSettingsConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<OrganizationModelSettings> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("OrganizationModelSettings");

        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.Id)
            .HasMaxLength(36);

        // ═══════════════════════════════════════════════════════════════
        // PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        _ = builder.Property(e => e.OrganizationId)
            .HasMaxLength(450);

        _ = builder.Property(e => e.UsageContext)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        _ = builder.Property(e => e.ModelPricingId)
            .HasMaxLength(36)
            .IsRequired();

        _ = builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        _ = builder.Property(e => e.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        _ = builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql(isPostgres ? "CURRENT_TIMESTAMP" : "GETUTCDATE()");

        // ═══════════════════════════════════════════════════════════════
        // RELATIONSHIPS
        // ═══════════════════════════════════════════════════════════════

        _ = builder.HasOne(e => e.Organization)
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        // ═══════════════════════════════════════════════════════════════
        // INDEXES
        // ═══════════════════════════════════════════════════════════════

        // Unique constraint: one setting per organization+context (null org = global)
        _ = builder.HasIndex(e => new { e.OrganizationId, e.UsageContext })
            .HasDatabaseName("IX_OrganizationModelSettings_OrgId_UsageContext")
            .IsUnique();

        // Lookup by organization
        _ = builder.HasIndex(e => e.OrganizationId)
            .HasDatabaseName("IX_OrganizationModelSettings_OrganizationId");

        // Lookup enabled settings
        _ = builder.HasIndex(e => new { e.IsEnabled, e.UsageContext })
            .HasDatabaseName("IX_OrganizationModelSettings_IsEnabled_UsageContext");
    }
}
