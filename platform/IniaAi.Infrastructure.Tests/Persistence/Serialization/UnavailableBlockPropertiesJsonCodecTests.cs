using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

/// <summary>
/// The database keeps a block whose type this build does not register. Its settings column reads
/// and writes back unchanged, so loading and saving the workflow loses nothing.
/// </summary>
public class UnavailableBlockPropertiesJsonCodecTests
{
    private const string SETTINGS_OF_AN_UNREGISTERED_TYPE = """{"type":1001,"threshold":3,"note":"{Earlier}","steps":[{"name":"a"}]}""";
    private const string SETTINGS_READ_FROM_A_WORKFLOW_FILE = """{"Type":1001,"Threshold":3}""";
    private const string SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED = """{"type":"aLaterType","threshold":3}""";

    [Theory]
    [MemberData(nameof(StoredSettings))]
    public void SettingsOfATypeNoRegistrationKnows_ComeBackUnchanged(string settings, string optionsName, BlockTypes expectedType)
    {
        JsonSerializerOptions options = optionsName == nameof(BlockPropertiesJsonDefaults.Options)
            ? BlockPropertiesJsonDefaults.Options
            : BlockPropertiesJsonDefaults.CoreOptions;

        BlockProperties read = JsonSerializer.Deserialize<BlockProperties>(settings, options)!;
        string written = JsonSerializer.Serialize(read, options);

        Assert.Equal(expectedType, read.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(settings), JsonNode.Parse(written)), written);
    }

    public static TheoryData<string, string, BlockTypes> StoredSettings()
    {
        return new()
        {
            { SETTINGS_OF_AN_UNREGISTERED_TYPE, nameof(BlockPropertiesJsonDefaults.Options), (BlockTypes)1001 },
            { SETTINGS_OF_AN_UNREGISTERED_TYPE, nameof(BlockPropertiesJsonDefaults.CoreOptions), (BlockTypes)1001 },
            { SETTINGS_READ_FROM_A_WORKFLOW_FILE, nameof(BlockPropertiesJsonDefaults.Options), (BlockTypes)1001 },
            { SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED, nameof(BlockPropertiesJsonDefaults.Options), UnavailableBlockProperties.UNNAMED_TYPE },
            { SETTINGS_OF_A_TYPE_A_LATER_RELEASE_NAMED, nameof(BlockPropertiesJsonDefaults.CoreOptions), UnavailableBlockProperties.UNNAMED_TYPE },
        };
    }
}
