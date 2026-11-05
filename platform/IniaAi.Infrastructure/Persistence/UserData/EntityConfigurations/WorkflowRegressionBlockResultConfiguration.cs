using IniaAi.Domain.Entities.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

public class WorkflowRegressionBlockResultConfiguration : IEntityTypeConfiguration<WorkflowRegressionBlockResult>
{
    private readonly DatabaseFacade _database;

    public WorkflowRegressionBlockResultConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<WorkflowRegressionBlockResult> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();
        bool isPostgres = providerName?.Contains("npgsql") == true;

        _ = builder.ToTable("WorkflowRegressionBlockResults");
        _ = builder.HasKey(e => e.Id);

        _ = builder.Property(e => e.OrganizationId).IsRequired();
        _ = builder.Property(e => e.WorkflowRegressionRunId).IsRequired();
        _ = builder.Property(e => e.Category).IsRequired().HasMaxLength(500);
        _ = builder.Property(e => e.Fact).IsRequired().HasMaxLength(1000);

        _ = builder.Property(e => e.Sources)
            .HasColumnType(isPostgres ? "jsonb" : "nvarchar(max)");

        _ = builder.Property(e => e.BaselineValue)
            .HasColumnType(isPostgres ? "text" : "nvarchar(max)");

        _ = builder.Property(e => e.RerunValue)
            .HasColumnType(isPostgres ? "text" : "nvarchar(max)");

        _ = builder.Property(e => e.SimilarityScore)
            .HasColumnType(isPostgres ? "numeric(5,2)" : "decimal(5,2)");

        _ = builder.Property(e => e.BaselineVariations)
            .HasColumnType(isPostgres ? "jsonb" : "nvarchar(max)");

        _ = builder.Property(e => e.RerunVariations)
            .HasColumnType(isPostgres ? "jsonb" : "nvarchar(max)");

        _ = builder.Property(e => e.BlockName).HasMaxLength(500);
        _ = builder.Property(e => e.BlockType).HasMaxLength(100);

        _ = builder.Property(e => e.DefinitionDiffSummary)
            .HasColumnType(isPostgres ? "text" : "nvarchar(max)");

        _ = builder.Property(e => e.BaselineNumeric)
            .HasColumnType(isPostgres ? "numeric(28,8)" : "decimal(28,8)");

        _ = builder.Property(e => e.RerunNumeric)
            .HasColumnType(isPostgres ? "numeric(28,8)" : "decimal(28,8)");

        _ = builder.Property(e => e.Tolerance)
            .HasColumnType(isPostgres ? "numeric(28,8)" : "decimal(28,8)");

        _ = builder.Property(e => e.SchemaFieldType)
            .HasMaxLength(50);

        // Index for efficient lookups by run
        _ = builder.HasIndex(e => e.WorkflowRegressionRunId)
            .HasDatabaseName("IX_WorkflowRegressionBlockResults_RunId");
    }
}
