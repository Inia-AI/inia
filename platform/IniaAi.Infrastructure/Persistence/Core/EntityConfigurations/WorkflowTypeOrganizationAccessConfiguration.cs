using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the WorkflowTypeOrganizationAccess entity.
/// </summary>
public class WorkflowTypeOrganizationAccessConfiguration : IEntityTypeConfiguration<WorkflowTypeOrganizationAccess>
{
    public void Configure(EntityTypeBuilder<WorkflowTypeOrganizationAccess> builder)
    {
        _ = builder.ToTable("WorkflowTypeOrganizationAccess");

        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.WorkflowTypeId)
            .IsRequired()
            .HasMaxLength(100);

        _ = builder.Property(x => x.OrganizationId)
            .IsRequired()
            .HasMaxLength(450);

        // Foreign key to WorkflowType (same database)
        _ = builder.HasOne(x => x.WorkflowType)
            .WithMany(wt => wt.OrganizationAccess)
            .HasForeignKey(x => x.WorkflowTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK constraint to Organization (cross-database reference)
        // OrganizationId is just a string reference to UserData.Organization.Id

        // Create unique index to prevent duplicate access grants
        _ = builder.HasIndex(x => new { x.WorkflowTypeId, x.OrganizationId })
            .IsUnique()
            .HasDatabaseName("IX_WorkflowTypeOrganizationAccess_WorkflowTypeId_OrganizationId");

        // Create index on OrganizationId for query performance
        _ = builder.HasIndex(x => x.OrganizationId)
            .HasDatabaseName("IX_WorkflowTypeOrganizationAccess_OrganizationId");
    }
}
