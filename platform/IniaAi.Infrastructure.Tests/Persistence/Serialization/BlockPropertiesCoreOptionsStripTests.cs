using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class BlockPropertiesCoreOptionsStripTests
{
    [Fact]
    public void CoreOptions_StripsExecutionState_FromPersistedTemplates()
    {
        var props = new AiQueryProperties { UserPromptTemplate = "Hello {tag}" };
        props.SetExecutionState(true, new Dictionary<string, string?> { ["tag"] = "world" });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.CoreOptions);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.CoreOptions);

        Assert.DoesNotContain("executionState", json, StringComparison.OrdinalIgnoreCase);
        var aiQueryProps = Assert.IsType<AiQueryProperties>(deserialized);
        Assert.Null(aiQueryProps.ExecutionState);
    }

    [Fact]
    public void UserDataOptions_KeepsExecutionState()
    {
        var props = new AiQueryProperties { UserPromptTemplate = "Hello {tag}" };
        props.SetExecutionState(true, new Dictionary<string, string?> { ["tag"] = "world" });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options);

        var aiQueryProps = Assert.IsType<AiQueryProperties>(deserialized);
        Assert.NotNull(aiQueryProps.ExecutionState);
    }
}
