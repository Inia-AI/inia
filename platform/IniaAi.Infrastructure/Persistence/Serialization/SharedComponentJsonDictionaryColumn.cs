using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace IniaAi.Infrastructure.Persistence.Serialization;

public static class SharedComponentJsonDictionaryColumn
{
    public static void Apply<TValue>(
        PropertyBuilder<Dictionary<string, TValue>?> property,
        string? providerName,
        JsonSerializerOptions options)
    {
        _ = property
            .HasConversion(
                v => ToStoredJson(v, options),
                v => string.IsNullOrEmpty(v)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, TValue>>(v, options))
            .HasColumnType(providerName != null && providerName.Contains("npgsql") ? "jsonb" : "nvarchar(max)");

        property.Metadata.SetValueComparer(new StoredJsonComparer<TValue>(options));
    }

    private static string? ToStoredJson<TValue>(Dictionary<string, TValue>? map, JsonSerializerOptions options)
    {
        return map == null || map.Count == 0
            ? null
            : JsonSerializer.Serialize(map, options);
    }

    private sealed class StoredJsonComparer<TValue> : ValueComparer<Dictionary<string, TValue>?>
    {
        private readonly JsonSerializerOptions _options;

        public StoredJsonComparer(JsonSerializerOptions options)
            : base(
                (c1, c2) => ToStoredJson(c1, options) == ToStoredJson(c2, options),
                c => c == null || c.Count == 0 ? 0 : ToStoredJson(c, options)!.GetHashCode(),
                c => c == null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, TValue>>(
                        JsonSerializer.Serialize(c, options), options))
        {
            _options = options;
        }

        /// <summary>
        /// The base object-typed Equals null-short-circuits before the equality lambda runs, and that
        /// object overload is the one EF change detection calls — it must be overridden or a null-loaded
        /// entity given an empty map still registers as modified.
        /// </summary>
        public override bool Equals(object? left, object? right)
        {
            return ToStoredJson(left as Dictionary<string, TValue>, _options)
                == ToStoredJson(right as Dictionary<string, TValue>, _options);
        }
    }
}
