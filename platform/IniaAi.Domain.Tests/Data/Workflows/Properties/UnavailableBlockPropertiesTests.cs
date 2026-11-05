using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

/// <summary>
/// A workflow file can hold a block whose type this build does not register: an extension's type,
/// or a closed module's. Its settings come back exactly as they were written, so saving the
/// workflow again loses nothing.
/// </summary>
public class UnavailableBlockPropertiesTests
{
    private const string SETTINGS_OF_AN_UNREGISTERED_TYPE = """{"Type":1001,"Threshold":3,"Note":"{Earlier}","Steps":[{"Name":"a"}]}""";

    [Fact]
    public void SettingsOfATypeNoRegistrationKnows_ComeBackUnchanged()
    {
        BlockProperties? read = BlockPropertiesTestCodec.DeserializeViaConverter(SETTINGS_OF_AN_UNREGISTERED_TYPE);

        Assert.NotNull(read);
        Assert.Equal((BlockTypes)1001, read.Type);
        Assert.True(
            JToken.DeepEquals(JObject.Parse(SETTINGS_OF_AN_UNREGISTERED_TYPE), JToken.Parse(JsonConvert.SerializeObject(read))),
            JsonConvert.SerializeObject(read));
    }

    [Fact]
    public void SettingsOfATypeALaterReleaseNamed_ComeBackUnchanged()
    {
        const string settings = """{"Type":"ALaterType","Threshold":3}""";

        BlockProperties? read = BlockPropertiesTestCodec.DeserializeViaConverter(settings);

        Assert.Equal(UnavailableBlockProperties.UNNAMED_TYPE, read?.Type);
        Assert.True(JToken.DeepEquals(JObject.Parse(settings), JToken.Parse(JsonConvert.SerializeObject(read))), JsonConvert.SerializeObject(read));
    }

    [Theory]
    [InlineData("""{"Type":1001,"IsCritical":true}""")]
    [InlineData("""{"type":1001,"isCritical":true}""")]
    public void TheStoredFailureSetting_StillStopsTheRun(string settings)
    {
        var block = new Block(BlockTypes.Merge, new Section("default", new Workflow())) { Type = (BlockTypes)1001 };
        block.Properties = BlockPropertiesTestCodec.DeserializeViaConverter(settings)!;

        Assert.True(block.AbortsExecutionOnFailure());
    }

    [Fact]
    public void SettingsOfATypeNoRegistrationKnows_ComeBackUnchanged_ThroughAJsonTree()
    {
        BlockProperties read = BlockPropertiesTestCodec.DeserializeViaConverter(SETTINGS_OF_AN_UNREGISTERED_TYPE)!;

        JToken tree = JToken.FromObject(new Dictionary<string, object> { ["Properties"] = read, ["Order"] = 1 });

        Assert.True(JToken.DeepEquals(JObject.Parse(SETTINGS_OF_AN_UNREGISTERED_TYPE), tree["Properties"]), tree.ToString());
        Assert.Equal(1, tree["Order"]!.Value<int>());
    }

    [Fact]
    public void AWorkflowSavedWithABlockOfATypeNoRegistrationKnows_KeepsTheBlocksSettings()
    {
        var workflow = new Workflow();
        var block = new Block(BlockTypes.Merge, new Section("default", workflow)) { Name = "Unknown" };
        block.Type = (BlockTypes)1001;
        block.Properties = BlockPropertiesTestCodec.DeserializeViaConverter(SETTINGS_OF_AN_UNREGISTERED_TYPE)!;

        JObject canonical = JObject.Parse(new WorkflowCanonicalizer().ToCanonicalJson(workflow));

        JToken stored = canonical.SelectToken("Sections[0].Blocks[0].Properties")!;
        Assert.True(JToken.DeepEquals(JObject.Parse(SETTINGS_OF_AN_UNREGISTERED_TYPE), stored), canonical.ToString());
    }

    [Fact]
    public void SettingsOfATypeNoRegistrationKnows_IgnoreAPlaceholderReplacement()
    {
        BlockProperties read = BlockPropertiesTestCodec.DeserializeViaConverter(SETTINGS_OF_AN_UNREGISTERED_TYPE)!;

        read.ReplaceAll(new Dictionary<string, string?> { ["{Earlier}"] = "replaced" });

        Assert.Empty(read.GetAllPlaceholders());
        Assert.True(JToken.DeepEquals(JObject.Parse(SETTINGS_OF_AN_UNREGISTERED_TYPE), JToken.Parse(JsonConvert.SerializeObject(read))));
    }
}
