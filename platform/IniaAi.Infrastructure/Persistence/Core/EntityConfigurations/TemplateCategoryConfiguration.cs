using IniaAi.Domain.Entities.Workflows.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the TemplateCategory entity.
/// </summary>
public class TemplateCategoryConfiguration : IEntityTypeConfiguration<TemplateCategory>
{
    public void Configure(EntityTypeBuilder<TemplateCategory> builder)
    {
        _ = builder.ToTable("TemplateCategories");

        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id)
                .HasMaxLength(100)
                .IsRequired()
                .ValueGeneratedNever();

        _ = builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

        _ = builder.Property(x => x.ParentCategoryId)
                .HasMaxLength(100)
                .IsRequired(false);

        _ = builder.Property(x => x.DisplayOrder)
                .HasDefaultValue(0)
                .IsRequired();

        // Self-referential relationship for parent category
        _ = builder.HasOne(x => x.ParentCategory)
               .WithMany()
               .HasForeignKey(x => x.ParentCategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        _ = builder.HasIndex(x => x.Name);
        _ = builder.HasIndex(x => x.ParentCategoryId);
        _ = builder.HasIndex(x => x.DisplayOrder);
    }
}
