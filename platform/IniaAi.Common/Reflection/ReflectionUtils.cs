using System.Reflection;

namespace IniaAi.Common.Reflection;

public static class ReflectionUtils
{
    public static string? GetPropertyValue(object obj, string propertyName)
    {
        if (obj is null || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        // Use reflection to get the property dynamically
        PropertyInfo? property = obj.GetType().GetProperty(propertyName);
        return property is null
            ? null
            : property.GetValue(obj)?.ToString() ?? string.Empty;
    }
}
