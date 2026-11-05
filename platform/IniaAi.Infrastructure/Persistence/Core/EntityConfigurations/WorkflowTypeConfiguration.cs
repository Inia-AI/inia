using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the WorkflowType entity.
/// </summary>
public class WorkflowTypeConfiguration : IEntityTypeConfiguration<WorkflowType>
{
    public void Configure(EntityTypeBuilder<WorkflowType> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id)
            .HasMaxLength(WorkflowType.ID_MAX_LENGTH)
            .IsRequired();

        _ = builder.Property(x => x.Name)
            .HasMaxLength(WorkflowType.NAME_MAX_LENGTH)
            .IsRequired();
    }
}
