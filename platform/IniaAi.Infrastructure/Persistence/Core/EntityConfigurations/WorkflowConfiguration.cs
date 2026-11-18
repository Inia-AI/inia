using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

/// <summary>
/// Configures the schema for the WorkflowType entity.
/// </summary>
public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    private readonly DatabaseFacade _database;

    public WorkflowConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();

        _ = builder.Property(x => x.WorkflowTypeId)
            .HasMaxLength(100)
            .IsRequired(false);

        _ = builder.OwnsMany(w => w.Variables, variableBuilder =>
        {
            _ = variableBuilder.Property(v => v.Options)
                .HasConversion(
                    v => v == null
                        ? null
                        : JsonSerializer.Serialize(v, JsonDefaults.OptionsWithOriginalKeys),
                    v => string.IsNullOrEmpty(v)
                        ? new Dictionary<string, string>()
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonDefaults.OptionsWithOriginalKeys)!)
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            variableBuilder.Property(v => v.Options)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, JsonDefaults.OptionsWithOriginalKeys) ==
                        JsonSerializer.Serialize(c2, JsonDefaults.OptionsWithOriginalKeys),
                    c => JsonSerializer.Serialize(c, JsonDefaults.OptionsWithOriginalKeys).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, string>>(
                        JsonSerializer.Serialize(c, JsonDefaults.OptionsWithOriginalKeys), JsonDefaults.OptionsWithOriginalKeys)!));

            VariableInputRulesColumn.Apply(variableBuilder.Property(v => v.InputRules), providerName);
        });

        _ = builder.HasMany(w => w.DocumentClasses)
            .WithOne(dc => dc.Workflow)
            .HasForeignKey(dc => dc.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure the Configuration property to be stored as JSON
        _ = builder.Property(x => x.Configuration)
            .HasConversion(
                v => v == null
                    ? null
                    : JsonSerializer.Serialize(v, JsonDefaults.Options),
                v => string.IsNullOrEmpty(v)
                    ? null
                    : JsonSerializer.Deserialize<Domain.Entities.Workflows.WorkflowConfiguration>(v, JsonDefaults.Options))
            .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

        // Add ValueComparer for proper change tracking of Configuration
        builder.Property(x => x.Configuration)
            .Metadata.SetValueComparer(new ValueComparer<Domain.Entities.Workflows.WorkflowConfiguration?>(
                (c1, c2) => (c1 == null && c2 == null) ||
                            (c1 != null && c2 != null &&
                            JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                            JsonSerializer.Serialize(c2, JsonDefaults.Options)),
                c => c == null ? 0 : JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                c => c == null ? null : JsonSerializer.Deserialize<Domain.Entities.Workflows.WorkflowConfiguration>(
                    JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options)));
    }
}
