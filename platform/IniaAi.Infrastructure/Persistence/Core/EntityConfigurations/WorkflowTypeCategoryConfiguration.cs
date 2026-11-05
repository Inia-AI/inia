using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the WorkflowCategory entity.
/// </summary>
public class WorkflowCategoryConfiguration : IEntityTypeConfiguration<WorkflowTypeCategory>
{
    public void Configure(EntityTypeBuilder<WorkflowTypeCategory> builder)
    {
        _ = builder.ToTable("WorkflowTypeCategories");

        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id)
            .HasMaxLength(100)
            .IsRequired();

        _ = builder.Property(x => x.Name)
            .HasMaxLength(250)
            .IsRequired();

        _ = builder.Property(x => x.Description)
            .HasMaxLength(1000);

        _ = builder.Property(x => x.DisplayOrder)
            .IsRequired(false);

        // Optional: one-to-many relationship with WorkflowTypes
        _ = builder.HasMany(x => x.WorkflowTypes)
               .WithOne(x => x.Category)
               .HasForeignKey(x => x.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
