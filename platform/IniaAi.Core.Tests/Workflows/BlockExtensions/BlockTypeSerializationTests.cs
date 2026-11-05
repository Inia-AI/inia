using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using IniaAi.Samples.WordCount;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.Workflows.BlockExtensions;

/// <summary>
/// The settings of every registered block type, the platform's and an extension's, come back as
/// the same class with the same content through the workflow file's serializer and the database's.
/// </summary>
public class BlockTypeSerializationTests
{
    public static TheoryData<int> RegisteredTypesWithSettings()
    {
        return [.. BlockTypeCatalog.All.Select(static definition => (int)definition.Type)];
    }

    [Theory]
    [MemberData(nameof(RegisteredTypesWithSettings))]
    public void Settings_RoundTripThroughTheWorkflowFileSerializer(int type)
    {
        BlockProperties written = BlockTypeCatalog.Find((BlockTypes)type)!.CreateDefaultProperties();
        string json = JsonConvert.SerializeObject(written);

        using JsonReader reader = JObject.Parse(json).CreateReader();
        BlockProperties? read = new BlockPropertiesNewtonsoftConverter().ReadJson(reader, typeof(BlockProperties), null, false, JsonSerializer.CreateDefault());

        Assert.NotNull(read);
        Assert.IsType(written.GetType(), read);
        Assert.Equal(json, JsonConvert.SerializeObject(read));
    }

    [Theory]
    [MemberData(nameof(RegisteredTypesWithSettings))]
    public void Settings_RoundTripThroughTheDatabaseSerializer(int type)
    {
        BlockProperties written = BlockTypeCatalog.Find((BlockTypes)type)!.CreateDefaultProperties();
        string json = System.Text.Json.JsonSerializer.Serialize(written, BlockPropertiesJsonDefaults.Options);

        BlockProperties? read = System.Text.Json.JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options);

        Assert.NotNull(read);
        Assert.IsType(written.GetType(), read);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(read, BlockPropertiesJsonDefaults.Options));
    }

    [Fact]
    public void TheSampleType_IsAmongTheRegisteredTypes()
    {
        Assert.NotNull(BlockTypeCatalog.Find(WordCountProperties.TYPE));
    }
}
