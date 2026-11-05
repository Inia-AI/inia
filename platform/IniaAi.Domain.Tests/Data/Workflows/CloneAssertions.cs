using System.Collections;
using System.Reflection;
using IniaAi.Common.Reflection;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Asserts that every non-[CloneIgnore] public writable property on the clone is equal to the source.
/// List<T> and Dictionary<TKey,TValue> must be different instances with the same contents
/// (duplication contract); everything else is compared with Equal (covers scalars, enums, and
/// reference types copied by reference — e.g. ExecutionState).
/// </summary>
internal static class CloneAssertions
{
    public static void AssertAllCloneableFieldsCopied<T>(T source, T clone) where T : class
    {
        Assert.NotSame(source, clone);

        IEnumerable<PropertyInfo> props = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead
                && p.CanWrite
                && p.GetCustomAttribute<CloneIgnoreAttribute>() is null);

        foreach (PropertyInfo prop in props)
        {
            object? sourceVal = prop.GetValue(source);
            object? cloneVal = prop.GetValue(clone);
            AssertFieldCopied(prop, sourceVal, cloneVal);
        }
    }

    private static void AssertFieldCopied(PropertyInfo prop, object? sourceVal, object? cloneVal)
    {
        Type t = prop.PropertyType;

        if (t.IsGenericType)
        {
            Type def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>))
            {
                AssertListCopied(sourceVal, cloneVal);
                return;
            }

            if (def == typeof(Dictionary<,>))
            {
                AssertDictionaryCopied(sourceVal, cloneVal);
                return;
            }
        }

        Assert.Equal(sourceVal, cloneVal);
    }

    private static void AssertListCopied(object? sourceVal, object? cloneVal)
    {
        if (sourceVal is null)
        {
            Assert.Null(cloneVal);
            return;
        }

        Assert.NotNull(cloneVal);
        Assert.NotSame(sourceVal, cloneVal);

        var sourceList = ((IEnumerable)sourceVal).Cast<object?>().ToList();
        var cloneList = ((IEnumerable)cloneVal).Cast<object?>().ToList();
        Assert.Equal(sourceList, cloneList);
    }

    private static void AssertDictionaryCopied(object? sourceVal, object? cloneVal)
    {
        if (sourceVal is null)
        {
            Assert.Null(cloneVal);
            return;
        }

        Assert.NotNull(cloneVal);
        Assert.NotSame(sourceVal, cloneVal);

        var sourceDict = (IDictionary)sourceVal;
        var cloneDict = (IDictionary)cloneVal;
        Assert.Equal(sourceDict.Count, cloneDict.Count);
        foreach (DictionaryEntry entry in sourceDict)
        {
            Assert.True(cloneDict.Contains(entry.Key));
            Assert.Equal(entry.Value, cloneDict[entry.Key]);
        }
    }
}
