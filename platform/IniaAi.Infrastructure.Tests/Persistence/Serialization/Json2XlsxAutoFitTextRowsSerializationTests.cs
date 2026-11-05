using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class Json2XlsxAutoFitTextRowsSerializationTests
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
        Assert.False(props.AutoFitTextRows);
    }

    [Fact]
    public void OptIn_SurvivesPersistenceRoundTrip()
    {
        var props = new Json2XlsxProperties { AutoFitTextRows = true };

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.CoreOptions);

        var roundTripped = Assert.IsType<Json2XlsxProperties>(deserialized);
        Assert.True(roundTripped.AutoFitTextRows);
    }
}
