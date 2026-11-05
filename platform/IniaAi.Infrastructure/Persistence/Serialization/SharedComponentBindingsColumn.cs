using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.Serialization;

/// <summary>
/// Column mapping for the SharedComponentBindings dictionary on Block and Section, shared by both
/// DbContexts so the four mapping sites cannot drift: original-casing JSON (binding keys are
/// replacement tags and must not be camelCased), a null column for null or empty maps, and a
/// comparer over that same stored form — null and empty maps compare equal, so setting an empty
/// map on a NULL-loaded entity never emits a no-op UPDATE, while in-place dictionary mutations
/// stay visible to the change tracker.
/// </summary>
public static class SharedComponentBindingsColumn
{
    public static void Apply(PropertyBuilder<Dictionary<string, string>?> property, string? providerName)
    {
        SharedComponentJsonDictionaryColumn.Apply(property, providerName, JsonDefaults.OptionsWithOriginalKeys);
    }
}
