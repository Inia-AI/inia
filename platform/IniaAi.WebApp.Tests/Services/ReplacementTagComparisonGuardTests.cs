using IniaAi.Common.Reflection;
using IniaAi.Domain.Abstractions.Entities;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// Anti-drift guard for the single case-sensitivity policy. Every optional
/// <c>stringComparison</c> parameter on the replacement-tag path must default to
/// <see cref="StringComparison.Ordinal"/> (via <c>ReplacementTags.Comparison</c>). The path is
/// defined by contract — every <see cref="ITemplatable"/> implementer plus the
/// <see cref="PlaceholderReflectionHelper"/> — not by assembly, so unrelated <c>stringComparison</c>
/// defaults elsewhere (e.g. PDF anonymisation) are correctly out of scope. A new placeholder-path
/// method that hard-codes a case-insensitive default re-scatters the policy and fails here.
/// Note: this only catches default parameter values, not explicit comparison arguments at call sites.
/// </summary>
public class ReplacementTagComparisonGuardTests
{
    [Fact]
    public void EveryStringComparisonDefault_OnPlaceholderPath_IsOrdinal()
    {
        Assembly[] assemblies =
        [
            typeof(ITemplatable).Assembly,                 // IniaAi.Domain
            typeof(PlaceholderReflectionHelper).Assembly,  // IniaAi.Common
        ];

        List<Type> placeholderTypes = [.. assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ITemplatable).IsAssignableFrom(t) || t == typeof(PlaceholderReflectionHelper))];

        // Sanity: the contract scan must actually find the placeholder types.
        Assert.Contains(typeof(PlaceholderReflectionHelper), placeholderTypes);
        Assert.True(placeholderTypes.Count > 5, "Expected several ITemplatable implementers to be scanned.");

        var offenders = new List<string>();

        foreach (Type type in placeholderTypes)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (MethodInfo method in methods)
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.Name != "stringComparison"
                        || !parameter.IsOptional
                        || parameter.ParameterType != typeof(StringComparison))
                    {
                        continue;
                    }

                    if (!Equals(parameter.DefaultValue, StringComparison.Ordinal))
                    {
                        offenders.Add($"{type.FullName}.{method.Name} (default = {parameter.DefaultValue})");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These placeholder-path stringComparison defaults are not Ordinal — point them at ReplacementTags.Comparison:\n"
                + string.Join("\n", offenders));
    }
}
