using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Infrastructure.Persistence.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;

public class SectionJsonColumnConfiguration : IEntityTypeConfiguration<Section>
{
    private readonly DatabaseFacade _database;

    public SectionJsonColumnConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<Section> builder)
    {
        string? providerName = _database.ProviderName?.ToLower();

        _ = builder.Property(x => x.Properties)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonDefaults.Options),
                v => string.IsNullOrEmpty(v)
                    ? new SectionProperties()
                    : JsonSerializer.Deserialize<SectionProperties>(v, JsonDefaults.Options) ?? new SectionProperties())
            .IsRequired()
            .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

        builder.Property(x => x.Properties)
            .Metadata.SetValueComparer(new ValueComparer<SectionProperties>(
                (c1, c2) => JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                            JsonSerializer.Serialize(c2, JsonDefaults.Options),
                c => JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                c => JsonSerializer.Deserialize<SectionProperties>(
                    JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options) ?? new SectionProperties()));

        _ = builder.Property(x => x.Configuration)
            .HasConversion(
                v => v == null
                    ? null
                    : JsonSerializer.Serialize(v, JsonDefaults.Options),
                v => string.IsNullOrEmpty(v)
                    ? null
                    : JsonSerializer.Deserialize<SectionConfiguration>(v, JsonDefaults.Options))
            .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

        builder.Property(x => x.Configuration)
            .Metadata.SetValueComparer(new ValueComparer<SectionConfiguration?>(
                (c1, c2) => (c1 == null && c2 == null) ||
                            (c1 != null && c2 != null &&
                            JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                            JsonSerializer.Serialize(c2, JsonDefaults.Options)),
                c => c == null ? 0 : JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                c => c == null ? null : JsonSerializer.Deserialize<SectionConfiguration>(
                    JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options)));

        SharedComponentBindingsColumn.Apply(builder.Property(x => x.SharedComponentBindings), providerName);
        SharedComponentSlotStatesColumn.Apply(builder.Property(x => x.SharedComponentSlotStates), providerName);

        _ = builder.HasIndex(x => x.SharedComponentId);
    }
}
