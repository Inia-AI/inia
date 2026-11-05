using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class Json2XlsxHideFromOutputSerializationTests
{
    [Fact]
    public void PersistedBlockWithoutTheField_ReadsAsOff()
    {
        const string LEGACY_JSON = """
            { "type": "Json2Xlsx", "jsonInputTemplate": "{data}", "conversionType": "HF Template" }
            """;

        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            LEGACY_JSON, BlockPropertiesJsonDefaults.CoreOptions);

        var props = Assert.IsType<Json2XlsxProperties>(deserialized);
        Assert.False(props.HideFromOutput);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptIn_SurvivesCoreAndUserDataPersistenceRoundTrip(bool useCoreOptions)
    {
        JsonSerializerOptions options = useCoreOptions
            ? BlockPropertiesJsonDefaults.CoreOptions
            : BlockPropertiesJsonDefaults.Options;
        var props = new Json2XlsxProperties { HideFromOutput = true };

        string json = JsonSerializer.Serialize<BlockProperties>(props, options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(json, options);

        var roundTripped = Assert.IsType<Json2XlsxProperties>(deserialized);
        Assert.True(roundTripped.HideFromOutput);
    }
}
