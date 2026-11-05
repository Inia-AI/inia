using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace IniaAi.Infrastructure.Persistence.Serialization;

/// <summary>
/// Stored JSON can hold an explicit null for a member its type declares non-nullable, such as the
/// axes a model leaves null on a pie chart. Reading skips that null, so the member keeps its default.
/// </summary>
public static class NonNullableMembers
{
    /// <summary>
    /// A <see cref="DefaultJsonTypeInfoResolver"/> modifier that ignores a JSON null for every
    /// non-nullable reference member of an object.
    /// </summary>
    public static void KeepDefaultOnNull(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.Set is not { } set
                || property.PropertyType.IsValueType
                || property.AttributeProvider is not PropertyInfo member
                || new NullabilityInfoContext().Create(member).WriteState != NullabilityState.NotNull)
            {
                continue;
            }

            property.Set = (target, value) =>
            {
                if (value is not null)
                {
                    set(target, value);
                }
            };
        }
    }
}
