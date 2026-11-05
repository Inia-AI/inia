using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using static IniaAi.Domain.Tests.Data.Workflows.Properties.BlockPropertiesTestCodec;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class AgentPropertiesOutputFilesTests
{
    private static Block AgentBlock(List<string>? outputFiles = null)
    {
        var block = new Block(BlockTypes.Agent, new Section("S", new Workflow("W")), "numbers_agent")
        {
            Name = "Numbers agent",
            Properties = new AgentProperties
            {
                WorkerSkills = [new AgentWorkerSkill { Name = "skill", Description = "Does it.", Content = "work" }],
                Model = "test-model-a",
            },
        };
        if (outputFiles is not null)
        {
            ((AgentProperties)block.Properties).OutputFiles = outputFiles;
        }

        return block;
    }

    [Fact]
    public void Default_IsEmptyAndNotWrittenToJson()
    {
        var props = new AgentProperties();

        string json = JsonConvert.SerializeObject(props);

        Assert.Empty(props.OutputFiles);
        Assert.DoesNotContain(nameof(AgentProperties.OutputFiles), json, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyJsonWithoutTheField_ReadsAsEmpty_AndWritesBackWithoutIt()
    {
        BlockProperties? deserialized = DeserializeViaConverter(
            """{"Type":28,"WorkerSkills":[{"Name":"skill","Description":"Does it.","Content":"work"}],"Model":"test-model-a"}""");

        var props = Assert.IsType<AgentProperties>(deserialized);
        Assert.Empty(props.OutputFiles);
        Assert.DoesNotContain(
            nameof(AgentProperties.OutputFiles), JsonConvert.SerializeObject(props), StringComparison.Ordinal);
    }

    [Fact]
    public void OptIn_SurvivesNewtonsoftRoundTripAndClone()
    {
        var props = new AgentProperties { OutputFiles = ["out/sub_1_rows.json", "out/totals.json"] };

        string json = JsonConvert.SerializeObject(props);
        var deserialized = Assert.IsType<AgentProperties>(DeserializeViaConverter(json));
        var clone = (AgentProperties)props.Clone();

        Assert.Contains("\"OutputFiles\":[\"out/sub_1_rows.json\",\"out/totals.json\"]", json, StringComparison.Ordinal);
        Assert.Equal(props.OutputFiles, deserialized.OutputFiles);
        Assert.Equal(props.OutputFiles, clone.OutputFiles);
        Assert.NotSame(props.OutputFiles, clone.OutputFiles);
    }

    [Fact]
    public void ExplicitNullInJson_ReadsAsEmpty()
    {
        var props = Assert.IsType<AgentProperties>(DeserializeViaConverter(
            """{"Type":28,"OutputFiles":null,"Model":"test-model-a"}"""));

        Assert.NotNull(props.OutputFiles);
        Assert.Empty(props.OutputFiles);
    }

    [Fact]
    public void DefaultBlock_WorkflowFileJsonAndLibraryHashCarryNoTraceOfTheField()
    {
        Block withDefault = AgentBlock();
        Block explicitlyEmpty = AgentBlock(outputFiles: []);
        var normalizer = new SharedComponentContentNormalizer(new WorkflowCanonicalizer());

        string definitionJson = normalizer.ToDefinitionJson(withDefault);

        Assert.DoesNotContain(nameof(AgentProperties.OutputFiles), withDefault.Section!.Workflow.ToExportJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AgentProperties.OutputFiles), definitionJson, StringComparison.Ordinal);
        Assert.Equal(normalizer.ComputeContentHash(withDefault), normalizer.ComputeContentHash(explicitlyEmpty));
        Assert.NotEqual(
            normalizer.ComputeContentHash(withDefault),
            normalizer.ComputeContentHash(AgentBlock(outputFiles: ["out/rows.json"])));
    }
}
