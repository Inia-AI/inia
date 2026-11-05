using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class EffortSerializationTests
{
    [Theory]
    [InlineData(ModelEffort.Low)]
    [InlineData(ModelEffort.Medium)]
    [InlineData(ModelEffort.High)]
    [InlineData(ModelEffort.XHigh)]
    [InlineData(ModelEffort.Max)]
    public void CoreOptions_Effort_RoundTrips(ModelEffort effort)
    {
        var original = new AiQueryProperties
        {
            Effort = effort,
        };

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.CoreOptions);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.CoreOptions)!;

        Assert.Contains("\"effort\"", json);
        Assert.Equal(effort, deserialized.Effort);
    }

    [Fact]
    public void CoreOptions_NoEffort_RoundTripsAsNull()
    {
        var original = new AiQueryProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.CoreOptions);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.CoreOptions)!;

        Assert.Null(deserialized.Effort);
    }

    [Fact]
    public void Options_Effort_RoundTripsOnUserDataPath()
    {
        var original = new AiQueryProperties
        {
            Effort = ModelEffort.Medium,
        };

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.Options);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options)!;

        Assert.Equal(ModelEffort.Medium, deserialized.Effort);
    }

    [Theory]
    [InlineData("xhigh", ModelEffort.XHigh)]
    [InlineData("xHigh", ModelEffort.XHigh)]
    [InlineData("low", ModelEffort.Low)]
    public void CoreOptions_EffortValue_ReadsCaseInsensitively(string jsonValue, ModelEffort expected)
    {
        string json = $"{{\"type\":\"aiQuery\",\"effort\":\"{jsonValue}\"}}";

        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.CoreOptions)!;

        Assert.Equal(expected, deserialized.Effort);
    }
}
