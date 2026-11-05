using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

public class WorkflowTypeCounterpartyTypeMapConfiguration : IEntityTypeConfiguration<WorkflowTypeCounterpartyTypeMap>
{
    public void Configure(EntityTypeBuilder<WorkflowTypeCounterpartyTypeMap> builder)
    {
        _ = builder.ToTable("WorkflowTypeCounterpartyTypeMap");

        _ = builder.HasKey(x => new { x.WorkflowTypeId, x.CounterpartyTypeId });

        _ = builder.Property(x => x.WorkflowTypeId)
               .IsRequired()
               .HasMaxLength(100);

        _ = builder.Property(x => x.CounterpartyTypeId)
               .IsRequired()
               .HasMaxLength(100);
    }
}
