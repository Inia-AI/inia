using IniaAi.Domain.Entities.Workflows;
using System.Collections;
using System.Reflection;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Anti-forgetting guard for <see cref="WorkflowConfiguration.Clone"/>: every public writable
/// member is filled with a non-default value by reflection, and the clone must carry each one
/// as its own instance. A member added to the class but not to <c>Clone()</c> fails here, before
/// a report-layout save can drop it in the editor.
/// </summary>
public class WorkflowConfigurationCloneGuardTests
{
    [Fact]
    public void Clone_CopiesEveryWritableMember_AsItsOwnInstance()
    {
        var source = new WorkflowConfiguration();
        IEnumerable<PropertyInfo> members = typeof(WorkflowConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite);

        foreach (PropertyInfo member in members)
        {
            member.SetValue(source, NonDefaultValueFor(member.PropertyType));
        }

        WorkflowConfiguration clone = source.Clone();

        foreach (PropertyInfo member in members)
        {
            object? sourceValue = member.GetValue(source);
            object? cloneValue = member.GetValue(clone);

            Assert.True(cloneValue is not null, $"{member.Name} was not cloned");

            if (member.PropertyType.IsClass && member.PropertyType != typeof(string))
            {
                Assert.True(!ReferenceEquals(sourceValue, cloneValue), $"{member.Name} is shared between the source and the clone");
            }
            else
            {
                Assert.Equal(sourceValue, cloneValue);
            }
        }
    }

    private static object NonDefaultValueFor(Type type)
    {
        if (type == typeof(string))
        {
            return "filled";
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(int))
        {
            return 1;
        }

        if (type.IsValueType)
        {
            throw new InvalidOperationException($"No non-default value for {type.Name}; extend the guard.");
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(type)!;
            dictionary["key"] = "value";
            return dictionary;
        }

        return Activator.CreateInstance(type)!;
    }
}
