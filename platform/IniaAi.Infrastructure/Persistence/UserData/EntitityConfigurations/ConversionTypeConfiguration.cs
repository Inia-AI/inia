using IniaAi.Domain.Entities.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the ConversionType entity.
/// </summary>
public class ConversionTypeConfiguration : IEntityTypeConfiguration<ConversionType>
{
    private const string LIVE_ROWS_POSTGRES = "\"IsDeleted\" = false";
    private const string LIVE_ROWS_OTHER = "[IsDeleted] = 0";

    private readonly DatabaseFacade _database;

    public ConversionTypeConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<ConversionType> builder)
    {
        _ = builder.HasKey(t => t.Id);

        _ = builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        _ = builder.Property(t => t.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        _ = builder.Property(t => t.OrganizationId)
            .IsRequired()
            .HasMaxLength(450);

        // A name is unique among the live conversion types of an organization. A deleted one
        // stays out of the index, so a name can be deleted, created again and deleted again.
        _ = builder.HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique()
            .HasFilter(_database.IsNpgsql() ? LIVE_ROWS_POSTGRES : LIVE_ROWS_OTHER);
    }
}
