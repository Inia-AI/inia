using IniaAi.Domain.Entities.Workflows.InputRules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Serialization;

/// <summary>
/// Column mapping for a workflow variable's input rules, shared by both DbContexts: JSON in one
/// column, and no value at all for a variable without rules, so the rows of existing variables do
/// not change. The property is set through its setter, which reads a missing value as no rules.
/// </summary>
public static class VariableInputRulesColumn
{
    public static void Apply(PropertyBuilder<List<VariableInputRule>> property, string? providerName)
    {
        _ = property
            .HasConversion(
                rules => ToStoredJson(rules),
                json => FromStoredJson(json))
            .HasColumnType(providerName != null && providerName.Contains("npgsql") ? "jsonb" : "nvarchar(max)")
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        property.Metadata.SetValueComparer(new ValueComparer<List<VariableInputRule>>(
            (left, right) => ToStoredJson(left) == ToStoredJson(right),
            rules => (ToStoredJson(rules) ?? string.Empty).GetHashCode(),
            rules => FromStoredJson(ToStoredJson(rules))));
    }

    private static string? ToStoredJson(List<VariableInputRule>? rules)
    {
        return rules is not { Count: > 0 }
            ? null
            : JsonSerializer.Serialize(rules, JsonDefaults.Options);
    }

    private static List<VariableInputRule> FromStoredJson(string? json)
    {
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<VariableInputRule>>(json, JsonDefaults.Options) ?? [];
    }
}
