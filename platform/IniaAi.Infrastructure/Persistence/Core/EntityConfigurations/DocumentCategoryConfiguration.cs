using IniaAi.Domain.Entities.Workflows.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the DocumentCategory entity.
/// </summary>
public class DocumentCategoryConfiguration : IEntityTypeConfiguration<DocumentCategory>
{
    public void Configure(EntityTypeBuilder<DocumentCategory> builder)
    {
        _ = builder.ToTable("DocumentCategories");

        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id)
                .HasMaxLength(100)
                .IsRequired()
                .ValueGeneratedNever();

        _ = builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

        _ = builder.Property(x => x.DisplayOrder)
                .HasDefaultValue(0)
                .IsRequired();

        _ = builder.HasIndex(x => x.Name);
        _ = builder.HasIndex(x => x.DisplayOrder);
    }
}
