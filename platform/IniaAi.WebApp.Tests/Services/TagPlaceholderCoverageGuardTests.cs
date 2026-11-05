using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// Anti-forgetting guard. Replacement-tag validation derives entirely from the runtime's own
/// placeholder discovery (<see cref="Block.GetAllPlaceholders"/> ->
/// <c>PlaceholderReflectionHelper</c>, type-based: string / IDictionary&lt;string,string&gt; /
/// List&lt;string&gt;). These tests cover the SAME flat shapes the production discovery handles:
/// they fail if a top-level template-shaped field on a BlockProperties type stops being
/// discovered, or if the block-level aggregation drops a source (Properties / OutputTitleTemplate
/// / TextInputs / RagSettings). They deliberately do NOT assert deeper shapes (a string nested in
/// a custom object, or a Dictionary&lt;string,CustomType&gt;) — those are invisible to the runtime
/// discovery too, so adding such a field is a design decision, not a silent regression. Fields
/// carrying <see cref="PlaceholderReplacementIgnoreAttribute"/> are declared opt-outs the
/// production filter must skip — for those the guard asserts the planted sentinel is NOT
/// discovered, so a weakened filter fails here too.
/// </summary>
public class TagPlaceholderCoverageGuardTests
{
    private const string SENTINEL = "__COVERAGE_SENTINEL__";

    /// <summary>
    /// The settings class of every block type the catalog holds. <see cref="UnavailableBlockProperties"/>
    /// is no type's settings: it keeps an unknown type's stored text, which nothing templates.
    /// </summary>
    public static IEnumerable<object[]> AllBlockPropertiesTypes()
    {
        return BlockTypeCatalog.All
            .Select(static definition => definition.PropertiesType)
            .Distinct()
            .Select(static type => new object[] { type });
    }

    [Theory]
    [MemberData(nameof(AllBlockPropertiesTypes))]
    public void EveryTemplateShapedField_IsDiscoveredByGetAllPlaceholders(Type propsType)
    {
        object? instance = Activator.CreateInstance(propsType);
        Assert.True(instance is BlockProperties, $"{propsType.Name} must be instantiable as BlockProperties");
        var props = (BlockProperties)instance!;

        var expected = new List<string>();
        var expectedIgnored = new List<string>();
        int counter = 0;

        foreach (PropertyInfo prop in propsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead)
            {
                continue;
            }

            bool isIgnored = prop.IsDefined(typeof(PlaceholderReplacementIgnoreAttribute), inherit: true);
            List<string> bucket = isIgnored ? expectedIgnored : expected;

            if (prop.PropertyType == typeof(string) && prop.CanWrite)
            {
                string token = $"{SENTINEL}_{counter++}";
                prop.SetValue(props, $"{{{token}}}");
                bucket.Add(token);
            }
            else if (typeof(IDictionary<string, string>).IsAssignableFrom(prop.PropertyType))
            {
                var dict = prop.GetValue(props) as IDictionary<string, string>;
                if (dict is null && prop.CanWrite)
                {
                    dict = new Dictionary<string, string>();
                    prop.SetValue(props, dict);
                }

                if (dict is not null)
                {
                    string token = $"{SENTINEL}_{counter++}";
                    dict[$"key_{counter}"] = $"{{{token}}}";
                    bucket.Add(token);
                }
            }
            else if (prop.PropertyType == typeof(List<string>))
            {
                var list = prop.GetValue(props) as List<string>;
                if (list is null && prop.CanWrite)
                {
                    list = [];
                    prop.SetValue(props, list);
                }

                if (list is not null)
                {
                    string token = $"{SENTINEL}_{counter++}";
                    list.Add($"{{{token}}}");
                    bucket.Add(token);
                }
            }
        }

        List<string> discovered = props.GetAllPlaceholders();

        foreach (string token in expected)
        {
            Assert.Contains(token, discovered);
        }

        foreach (string token in expectedIgnored)
        {
            Assert.DoesNotContain(token, discovered);
        }
    }

    [Fact]
    public void BlockGetAllPlaceholders_AggregatesEveryKnownSource()
    {
        // If a future change adds a new template-bearing collection to Block but forgets to
        // wire it into GetAllPlaceholders, the corresponding sentinel disappears and this fails.
        var workflow = new Workflow("guard-workflow");
        var section = new Section("guard-section", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            Id = Guid.NewGuid().ToString(),
            ReplacementTag = "guard_block",
            OutputTitleTemplate = $"{{{SENTINEL}_title}}",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"{{{SENTINEL}_props}}",
            },
        };

        _ = new TextInput(block, "guard_ti", $"{{{SENTINEL}_textinput}}");
        block.RagSettings.Add(new RagSettings
        {
            QueryTemplate = $"{{{SENTINEL}_rag}}",
        });

        List<string> discovered = block.GetAllPlaceholders();

        Assert.Contains($"{SENTINEL}_title", discovered);
        Assert.Contains($"{SENTINEL}_props", discovered);
        Assert.Contains($"{SENTINEL}_textinput", discovered);
        Assert.Contains($"{SENTINEL}_rag", discovered);
    }
}
