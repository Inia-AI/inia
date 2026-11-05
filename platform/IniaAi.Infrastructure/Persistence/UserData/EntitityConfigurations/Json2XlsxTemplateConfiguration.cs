using IniaAi.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the Json2XlsxTemplate entity.
/// </summary>
public class Json2XlsxTemplateConfiguration : IEntityTypeConfiguration<Json2XlsxTemplate>
{
    public void Configure(EntityTypeBuilder<Json2XlsxTemplate> builder)
    {
        _ = builder.HasKey(t => t.Id);

        _ = builder.Property(t => t.OrganizationId)
            .IsRequired();

        _ = builder.Property(t => t.ConversionType)
            .IsRequired()
            .HasMaxLength(100);

        _ = builder.Property(t => t.FileMetadataId)
            .IsRequired();

        _ = builder.Property(t => t.OriginalFileName)
            .IsRequired()
            .HasMaxLength(450);

        _ = builder.Property(t => t.CreatedByUserId);

        // One template per org + conversion type
        _ = builder.HasIndex(t => new { t.OrganizationId, t.ConversionType })
            .IsUnique();

        _ = builder.HasOne(t => t.FileMetadata)
            .WithMany()
            .HasForeignKey(t => t.FileMetadataId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
