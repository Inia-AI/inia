using IniaAi.Domain.Entities.Counterparties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Serialization.Counterparties;

/// <summary>
/// Column mapping for the freeform <see cref="Counterparty.Properties"/> bag: original-casing JSON
/// (author-coined keys must not be camelCased), a null column for a null or empty bag, and
/// materialization into a case-insensitive map via <see cref="CounterpartyPropertyBag"/> so the feature's
/// one invariant holds however the row was written. Deliberately separate from SharedComponentBindingsColumn,
/// whose replacement-tag keys stay case-sensitive.
/// </summary>
public static class CounterpartyPropertiesColumn
{
    public static void Apply(PropertyBuilder<Dictionary<string, string>?> property, string? providerName)
    {
        _ = property
            .HasConversion(
                bag => ToStoredJson(bag),
                json => FromStoredJson(json))
            .HasColumnType(providerName != null && providerName.Contains("npgsql") ? "jsonb" : "nvarchar(max)");

        property.Metadata.SetValueComparer(new PropertiesComparer());
    }

    private static string? ToStoredJson(Dictionary<string, string>? bag)
    {
        return bag == null || bag.Count == 0
            ? null
            : JsonSerializer.Serialize(bag, JsonDefaults.OptionsWithOriginalKeys);
    }

    private static Dictionary<string, string>? FromStoredJson(string? json)
    {
        return string.IsNullOrEmpty(json)
            ? null
            : CounterpartyPropertyBag.Normalize(
                JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.OptionsWithOriginalKeys));
    }

    /// <summary>
    /// The base object-typed Equals null-short-circuits before the equality lambda, and EF change
    /// detection calls that overload — it must be overridden or a null-loaded bag given an empty map
    /// still registers as modified (a no-op UPDATE). Null and empty bags compare equal.
    /// </summary>
    private sealed class PropertiesComparer : ValueComparer<Dictionary<string, string>?>
    {
        public PropertiesComparer()
            : base(
                (left, right) => ToStoredJson(left) == ToStoredJson(right),
                bag => bag == null || bag.Count == 0 ? 0 : ToStoredJson(bag)!.GetHashCode(),
                bag => FromStoredJson(ToStoredJson(bag)))
        {
        }

        public override bool Equals(object? left, object? right)
        {
            return ToStoredJson(left as Dictionary<string, string>) == ToStoredJson(right as Dictionary<string, string>);
        }
    }
}
