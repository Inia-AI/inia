using System.Reflection;
using IniaAi.Common.Reflection;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows;

public class ParameterizedSectionTemplateCoverageTests
{
    private static readonly ParameterizedSectionConfig _config = new(
    [
        new SectionVariant(
            Key: "AR",
            Label: "Annual Report",
            Params: new() { ["DOC_NAME"] = "Annual Report" }),
        new SectionVariant(
            Key: "IR",
            Label: "Interim Report",
            Params: new() { ["DOC_NAME"] = "Interim Report" }),
    ]);

    [Fact]
    public void Expand_BlockTextInputAndFilterRuleFields_GetSiblingSuffixAndParameterValue()
    {
        (Section template, Block reader) = CreateSectionWithSiblingAndReader(BlockTypes.Merge);
        _ = new TextInput(reader, "AF11", "{sibling.stop} for {DOC_NAME}");
        _ = new RagSettings(reader)
        {
            Keywords = ["{DOC_NAME}", "{sibling.company}"],
            SmartFilterExpressions = ["{sibling.pages} in {DOC_NAME}"],
        };

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, _config);

        Block annualReader = expanded[0].Blocks.Single(b => b.ReplacementTag == "reader_AR");
        Assert.Equal("{sibling_AR.stop} for Annual Report", annualReader.TextInputs.Single().Value);
        RagSettings annualRag = annualReader.RagSettings.Single();
        Assert.Equal(["Annual Report", "{sibling_AR.company}"], annualRag.Keywords);
        Assert.Equal(["{sibling_AR.pages} in Annual Report"], annualRag.SmartFilterExpressions);

        Block interimReader = expanded[1].Blocks.Single(b => b.ReplacementTag == "reader_IR");
        Assert.Equal("{sibling_IR.stop} for Interim Report", interimReader.TextInputs.Single().Value);
    }

    public static TheoryData<BlockTypes> AllBlockTypes()
    {
        return [.. Enum.GetValues<BlockTypes>()];
    }

    [Theory]
    [MemberData(nameof(AllBlockTypes))]
    public void Expand_EveryAuthoredStringProperty_IsRewrittenForTheVariant(BlockTypes type)
    {
        (Section template, Block reader) = CreateSectionWithSiblingAndReader(type);
        List<PropertyInfo> authoredStrings = AuthoredStringProperties(reader.Properties);
        foreach (PropertyInfo property in authoredStrings)
        {
            property.SetValue(reader.Properties, "{sibling} {DOC_NAME}");
        }

        List<Section> expanded = ParameterizedSectionExpander.Expand(template, _config);

        Block annualReader = expanded[0].Blocks.Single(b => b.ReplacementTag == "reader_AR");
        foreach (PropertyInfo property in authoredStrings)
        {
            Assert.True(
                (string?)property.GetValue(annualReader.Properties) == "{sibling_AR} Annual Report",
                $"{type}.{property.Name} was not rewritten for the variant: '{property.GetValue(annualReader.Properties)}'");
        }
    }

    private static (Section Template, Block Reader) CreateSectionWithSiblingAndReader(BlockTypes readerType)
    {
        var workflow = new Workflow("Parameterized coverage");
        var section = new Section("Extract", workflow);
        _ = new Block(BlockTypes.Merge, section, "sibling")
        {
            Properties = new MergeProperties { TextTemplate = "sibling output" },
        };
        var reader = new Block(readerType, section, "reader");
        return (section, reader);
    }

    private static List<PropertyInfo> AuthoredStringProperties(BlockProperties properties)
    {
        return [.. properties.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite)
            .Where(p => !p.IsDefined(typeof(PlaceholderReplacementIgnoreAttribute), inherit: true))
            .Where(p => !p.IsDefined(typeof(NotAReplacementTemplateAttribute), inherit: true))];
    }
}
