using IniaAi.Domain.Entities.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the ConversionType entity.
/// </summary>
public class ConversionTypeConfiguration : IEntityTypeConfiguration<ConversionType>
{
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
    }
}
