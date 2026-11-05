using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class AgentOutputFilesSerializationTests
{
    [Fact]
    public void PersistedBlockWithoutTheField_ReadsAsEmpty()
    {
        const string LEGACY_JSON = """
            { "type": "Agent", "promptTemplate": "Do the work.", "model": "test-model-a" }
            """;

        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            LEGACY_JSON, BlockPropertiesJsonDefaults.CoreOptions);

        var props = Assert.IsType<AgentProperties>(deserialized);
        Assert.Empty(props.OutputFiles);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptIn_SurvivesCoreAndUserDataPersistenceRoundTrip(bool useCoreOptions)
    {
        JsonSerializerOptions options = useCoreOptions
            ? BlockPropertiesJsonDefaults.CoreOptions
            : BlockPropertiesJsonDefaults.Options;
        var props = new AgentProperties { OutputFiles = ["out/sub_1_rows.json", "out/checks/totals.json"] };

        string json = JsonSerializer.Serialize<BlockProperties>(props, options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(json, options);

        var roundTripped = Assert.IsType<AgentProperties>(deserialized);
        Assert.Equal(["out/sub_1_rows.json", "out/checks/totals.json"], roundTripped.OutputFiles);
    }
}
