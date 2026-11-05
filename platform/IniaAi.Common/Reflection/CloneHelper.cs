using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace IniaAi.Common.Reflection;

public static class CloneHelper
{
    private static readonly ConcurrentDictionary<Type, CopyablePropertyInfo[]> _propsCache = new();

    public static void CopyScalars<T>(T source, T target) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (CopyablePropertyInfo copyable in GetCopyableProperties(source.GetType()))
        {
            object? value = copyable.Property.GetValue(source);
            object? copied = copyable.CopyMode switch
            {
                CopyMode.DuplicateList => DuplicateList(value, copyable.ConstructedType!),
                CopyMode.DuplicateDictionary => DuplicateDictionary(value, copyable.ConstructedType!),
                _ => value,
            };

            copyable.Property.SetValue(target, copied);
        }
    }

    private static object? DuplicateList(object? source, Type listType)
    {
        if (source is null)
        {
            return null;
        }

        var copy = (IList)Activator.CreateInstance(listType)!;
        foreach (object? item in (IEnumerable)source)
        {
            _ = copy.Add(item);
        }

        return copy;
    }

    private static object? DuplicateDictionary(object? source, Type dictType)
    {
        if (source is null)
        {
            return null;
        }

        var copy = (IDictionary)Activator.CreateInstance(dictType)!;
        foreach (DictionaryEntry entry in (IDictionary)source)
        {
            copy.Add(entry.Key, entry.Value);
        }

        return copy;
    }

    private static CopyablePropertyInfo[] GetCopyableProperties(Type type)
    {
        return _propsCache.GetOrAdd(type, static t =>
            [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod?.IsPublic == true
                    && p.SetMethod?.IsPublic == true
                    && p.GetCustomAttribute<CloneIgnoreAttribute>() is null)
                .Select(BuildCopyable)]);
    }

    private static CopyablePropertyInfo BuildCopyable(PropertyInfo prop)
    {
        Type t = prop.PropertyType;

        if (t == typeof(string))
        {
            return new CopyablePropertyInfo(prop, CopyMode.Assign, null);
        }

        if (t.IsGenericType)
        {
            Type def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>))
            {
                return new CopyablePropertyInfo(prop, CopyMode.DuplicateList, t);
            }

            if (def == typeof(Dictionary<,>))
            {
                return new CopyablePropertyInfo(prop, CopyMode.DuplicateDictionary, t);
            }
        }

        if (IsCollectionLike(t))
        {
            throw new InvalidOperationException(
                $"CloneHelper cannot safely copy property '{prop.DeclaringType?.FullName}.{prop.Name}' "
                + $"of type '{t}'. Collection-shaped types must be either a concrete List<T> or "
                + "Dictionary<TKey,TValue> so the helper can duplicate them, or marked with "
                + "[CloneIgnore] and handled explicitly by the caller.");
        }

        return new CopyablePropertyInfo(prop, CopyMode.Assign, null);
    }

    private static bool IsCollectionLike(Type t)
    {
        return t.IsArray || typeof(IEnumerable).IsAssignableFrom(t);
    }

    private enum CopyMode
    {
        Assign,
        DuplicateList,
        DuplicateDictionary,
    }

    private sealed record CopyablePropertyInfo(PropertyInfo Property, CopyMode CopyMode, Type? ConstructedType);
}
