using IniaAi.Domain.Entities.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

public class WorkflowRegressionRunConfiguration : IEntityTypeConfiguration<WorkflowRegressionRun>
{
    private readonly DatabaseFacade _database;

    public WorkflowRegressionRunConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<WorkflowRegressionRun> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("WorkflowRegressionRuns");
        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.OrganizationId).IsRequired();
        _ = builder.Property(e => e.BaselineExecutionId).IsRequired();
        _ = builder.Property(e => e.TriggeredByUserId).IsRequired();

        _ = builder.Property(e => e.WorkflowTypeName).HasMaxLength(500);
        _ = builder.Property(e => e.CounterpartyName).HasMaxLength(500);

        _ = builder.Property(e => e.OverallSimilarityPercent)
            .HasColumnType(isPostgres ? "numeric(5,2)" : "decimal(5,2)");

        _ = builder.Property(e => e.BatchId).HasMaxLength(100);
        _ = builder.Property(e => e.ErrorMessage).HasMaxLength(4000);

        // Indexes
        _ = builder.HasIndex(e => new { e.OrganizationId, e.Status })
            .HasDatabaseName("IX_WorkflowRegressionRuns_OrgId_Status");

        _ = builder.HasIndex(e => e.BaselineExecutionId)
            .HasDatabaseName("IX_WorkflowRegressionRuns_BaselineExecutionId");

        _ = builder.HasIndex(e => new { e.OrganizationId, e.BatchId })
            .HasDatabaseName("IX_WorkflowRegressionRuns_OrgId_BatchId");

        // One-to-many: WorkflowRegressionRun -> WorkflowRegressionBlockResult
        _ = builder.HasMany(e => e.BlockResults)
            .WithOne(e => e.WorkflowRegressionRun)
            .HasForeignKey(e => e.WorkflowRegressionRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
